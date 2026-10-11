using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Application;

public enum CollectorState { Starting, Running, Paused, Suspended, Degraded, Failed, Stopped }

public sealed record CollectorSnapshot(
    long Sequence,
    CollectorState State,
    Reading Reading,
    AdapterObservation Observation)
{
    public CanonicalScopeDecision? ScopeDecision { get; init; }
    public StorageStatus? StorageStatus { get; init; }
}

/// <summary>
/// Serializes all accounting before replacing the single latest display snapshot.
/// Connects RouteProvider, ScopePolicy, and StorageEngine into the canonical accounting stream.
/// </summary>
public sealed class Collector : IDisposable
{
    private readonly object gate = new();
    private readonly IAdapterCounterProvider provider;
    private readonly IMeasurementClock clock;
    private readonly IRouteProvider? routeProvider;
    private readonly IStorageEngine? storage;
    private readonly ScopeStabilizer? stabilizer;
    private readonly MeasurementEngine engine = new();
    private readonly CancellationTokenSource cancellation = new();

    private AdapterObservation observation = new(ProviderAvailability.TemporarilyUnavailable, [],
        "not-yet-observed", "starting", "Waiting for the first provider observation.");
    private CollectorSnapshot latest;
    private ScopeMode scopeMode = ScopeMode.SelectedAdapter;
    private CanonicalScopeDecision scopeDecision;
    private string? scope;
    private string sessionId;
    private string clockSegmentId;
    private string scopeRevisionId;
    private long revisionNumber;
    private bool paused;
    private bool suspended;
    private bool disposed;
    private double interval = 1;
    private long sequence;
    private Task? worker;
    private CollectorSnapshot? pendingNotification;
    private bool notifying;
    private long subscriberFailures;
    private bool sessionStartedInStorage;

    public event Action<CollectorSnapshot>? Updated;

    public Collector(
        IAdapterCounterProvider provider,
        IMeasurementClock clock,
        IRouteProvider? routeProvider = null,
        IStorageEngine? storage = null,
        double routeDebounceSeconds = 3.0)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.routeProvider = routeProvider;
        this.storage = storage;

        sessionId = Guid.NewGuid().ToString("D");
        clockSegmentId = Guid.NewGuid().ToString("D");
        scopeRevisionId = Guid.NewGuid().ToString("D");
        revisionNumber = 1;

        scopeDecision = new CanonicalScopeDecision(
            Guid.NewGuid(), 1, ScopeMode.SelectedAdapter, TopologyState.SelectedAdapterMissing,
            [], null, "No adapter selected", "Choose an adapter from the interface inventory.", false, DateTimeOffset.UtcNow);

        if (routeProvider is not null)
        {
            scopeMode = ScopeMode.AutoPrimary;
            var initialCandidates = routeProvider.GetCandidates();
            scopeDecision = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, initialCandidates);
            stabilizer = new ScopeStabilizer(scopeDecision, routeDebounceSeconds);
            scope = scopeDecision.PrimaryAdapterId;
            scopeRevisionId = scopeDecision.ScopeId.ToString("D") + "_" + scopeDecision.Revision;
            revisionNumber = scopeDecision.Revision;
            if (scope is not null)
            {
                engine.SelectScope(scope);
            }
        }

        latest = new(0, CollectorState.Starting,
            engine.Interrupt(clock.Read().UtcNow, ReadingState.Measuring,
                scope is null ? "Choose an adapter to begin." : "Establishing baseline for primary adapter."),
            observation)
        {
            ScopeDecision = scopeDecision
        };
    }

    public CollectorSnapshot Latest { get { lock (gate) return latest; } }
    public CanonicalScopeDecision ScopeDecision { get { lock (gate) return scopeDecision; } }
    public ScopeMode Mode { get { lock (gate) return scopeMode; } }
    public long SubscriberFailures => Interlocked.Read(ref subscriberFailures);
    public bool Paused { get { lock (gate) return paused; } }
    public double Interval
    {
        get { lock (gate) return interval; }
        set
        {
            if (value is not (0.5 or 1 or 2)) throw new ArgumentOutOfRangeException(nameof(value), "Supported intervals are 0.5, 1 and 2 seconds.");
            lock (gate) { ThrowIfDisposed(); interval = value; }
        }
    }

    public void Start()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (worker is not null) return;
            worker = Task.Run(RunAsync);
        }
    }

    public void Select(string? id)
    {
        SetScope(ScopeMode.SelectedAdapter, id);
    }

    public void SetScope(ScopeMode mode, string? selectedAdapterId = null)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            scopeMode = mode;
            var candidates = routeProvider?.GetCandidates() ?? [];
            var newDecision = ScopePolicy.Evaluate(mode, selectedAdapterId, candidates, scopeDecision);

            if (stabilizer is not null && mode == ScopeMode.AutoPrimary)
            {
                var time = clock.Read();
                newDecision = stabilizer.Update(newDecision, time.MonotonicSeconds);
            }

            scopeDecision = newDecision;
            scope = newDecision.PrimaryAdapterId;
            scopeRevisionId = newDecision.ScopeId.ToString("D") + "_" + newDecision.Revision;
            revisionNumber = newDecision.Revision;

            engine.RebaselineForScopeChange(scope);

            if (storage is not null)
            {
                RecordScopeToStorage(newDecision);
            }

            PublishInterruption($"Scope mode set to {mode}. Establishing baseline.");
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            engine.ResetSession();
            PublishInterruption("Session reset. Establishing a new baseline.");
        }
    }

    public void TogglePause()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            paused = !paused;
            var time = clock.Read();

            if (paused && storage is not null)
            {
                var gap = new UsageIntervalRecord(
                    Guid.NewGuid().ToString("D"),
                    $"{sessionId}_gap_{sequence}",
                    sessionId,
                    clockSegmentId,
                    scopeRevisionId,
                    "unidentified",
                    time.UtcNow.ToUnixTimeMilliseconds() - (long)(interval * 1000),
                    time.UtcNow.ToUnixTimeMilliseconds(),
                    (long)(interval * 10_000_000),
                    0, 0, false, true, "user_paused", "nominal");
                _ = storage.IngestGapAsync(gap);
            }

            PublishInterruption(paused ? "Paused · this interval is not observed." : "Resuming. Establishing a new baseline.");
        }
    }

    public void SetSuspended(bool value)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            suspended = value;
            var time = clock.Read();

            if (suspended && storage is not null)
            {
                var gap = new UsageIntervalRecord(
                    Guid.NewGuid().ToString("D"),
                    $"{sessionId}_sleep_{sequence}",
                    sessionId,
                    clockSegmentId,
                    scopeRevisionId,
                    "unidentified",
                    time.UtcNow.ToUnixTimeMilliseconds() - (long)(interval * 1000),
                    time.UtcNow.ToUnixTimeMilliseconds(),
                    (long)(interval * 10_000_000),
                    0, 0, false, true, "system_sleep", "nominal");
                _ = storage.IngestGapAsync(gap);
            }

            if (!suspended && routeProvider is not null && scopeMode == ScopeMode.AutoPrimary)
            {
                // Re-evaluate routes on system resume
                var candidates = routeProvider.GetCandidates();
                var evaluated = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, candidates, scopeDecision);
                if (stabilizer is not null)
                {
                    scopeDecision = stabilizer.Update(evaluated, time.MonotonicSeconds);
                }
                else
                {
                    scopeDecision = evaluated;
                }
                scope = scopeDecision.PrimaryAdapterId;
                scopeRevisionId = scopeDecision.ScopeId.ToString("D") + "_" + scopeDecision.Revision;
                engine.RebaselineForScopeChange(scope);
            }

            PublishInterruption(value ? "System suspended · monitoring gap." : "System resumed. Establishing a new baseline.");
        }
    }

    private CollectorSnapshot PublishInterruption(string runningDetail)
    {
        var state = suspended ? CollectorState.Suspended : paused ? CollectorState.Paused : CollectorState.Starting;
        var detail = suspended ? "System suspended · monitoring gap." : paused ? "Paused · this interval is not observed." : runningDetail;
        return Publish(state, engine.Interrupt(clock.Read().UtcNow, suspended || paused ? ReadingState.Paused : ReadingState.Measuring, detail));
    }

    /// <summary>One serialized real or test observation; usable without starting a timer.</summary>
    public CollectorSnapshot SampleOnce()
    {
        CollectorSnapshot snapshot;
        lock (gate)
        {
            ThrowIfDisposed();
            if (paused || suspended) return latest;

            var time = clock.Read();

            // AutoPrimary dynamic route evaluation with flapping stabilization
            if (routeProvider is not null && scopeMode == ScopeMode.AutoPrimary)
            {
                var candidates = routeProvider.GetCandidates();
                var evaluated = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, candidates, scopeDecision);
                var stabilized = stabilizer is not null ? stabilizer.Update(evaluated, time.MonotonicSeconds) : evaluated;

                if (stabilized.Revision != scopeDecision.Revision || stabilized.PrimaryAdapterId != scope)
                {
                    scopeDecision = stabilized;
                    scope = stabilized.PrimaryAdapterId;
                    scopeRevisionId = stabilized.ScopeId.ToString("D") + "_" + stabilized.Revision;
                    revisionNumber = stabilized.Revision;

                    engine.RebaselineForScopeChange(scope);

                    if (storage is not null)
                    {
                        RecordScopeToStorage(stabilized);
                    }
                }
            }

            try { observation = provider.ReadSnapshot(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                observation = new(ProviderAvailability.Failed, [], observation.Method, "provider.unexpected-failure",
                    "The adapter provider failed. Monitoring is unavailable; retry will occur on the next sample.");
            }

            if (observation.Availability != ProviderAvailability.Available)
            {
                var state = observation.Availability == ProviderAvailability.Failed ? CollectorState.Failed : CollectorState.Degraded;
                snapshot = Publish(state, engine.Interrupt(time.UtcNow, ReadingState.Unavailable, observation.Detail, time.MonotonicSeconds));
            }
            else
            {
                var adapter = observation.Adapters.FirstOrDefault(a => a.Id == scope);
                var reading = engine.Sample(adapter, time.MonotonicSeconds, time.UtcNow, interval);

                // Persist valid interval and checkpoint if storage is attached
                if (storage is not null && reading.State == ReadingState.Live && reading.IntervalBytesRx.HasValue && reading.IntervalBytesTx.HasValue)
                {
                    EnsureStorageSessionInitialized(time);
                    PersistInterval(reading, adapter, time);
                }

                var state = reading.State switch
                {
                    ReadingState.Live => storage is not null && storage.Status.Health != StorageHealth.Healthy
                        ? CollectorState.Degraded
                        : CollectorState.Running,
                    ReadingState.Measuring => CollectorState.Starting,
                    _ => CollectorState.Degraded
                };

                snapshot = Publish(state, reading);
            }
        }
        return snapshot;
    }

    private void PersistInterval(Reading reading, AdapterCounter? adapter, ClockReading time)
    {
        var durationTicks = (long)((reading.IntervalDurationSeconds ?? interval) * 10_000_000);
        var endUtcMs = reading.Timestamp.ToUnixTimeMilliseconds();
        var startUtcMs = endUtcMs - (long)((reading.IntervalDurationSeconds ?? interval) * 1000);

        try
        {
            var rxSigned = CheckedIntegerMath.CheckedToSigned(reading.IntervalBytesRx!.Value, "rx delta");
            var txSigned = CheckedIntegerMath.CheckedToSigned(reading.IntervalBytesTx!.Value, "tx delta");

            var intervalRec = new UsageIntervalRecord(
                Guid.NewGuid().ToString("D"),
                $"{sessionId}_{reading.MeasurementEpoch}_{sequence}",
                sessionId,
                clockSegmentId,
                scopeRevisionId,
                "unidentified",
                startUtcMs,
                endUtcMs,
                durationTicks,
                rxSigned,
                txSigned,
                reading.Coarse,
                false,
                null,
                "nominal");

            CounterCheckpointRecord? cp = null;
            if (adapter is not null)
            {
                cp = new CounterCheckpointRecord(
                    Guid.NewGuid().ToString("D"),
                    sessionId,
                    adapter.Id,
                    "iphlpapi.GetIfTable2",
                    adapter.SourceEpoch.Value.ToString("D"),
                    sequence,
                    (long)(time.MonotonicSeconds * 10_000_000),
                    endUtcMs,
                    BigEndianCodec.EncodeUInt64(adapter.Received),
                    BigEndianCodec.EncodeUInt64(adapter.Sent));
            }

            // Atomic ingestion: interval and checkpoint committed in the same transaction
            _ = storage!.IngestIntervalAsync(intervalRec, cp);
        }
        catch (StorageCapacityExceededException)
        {
            // Capacity exceeded: rebaseline to halt delta contamination and record explicit gap
            engine.Interrupt(time.UtcNow, ReadingState.Discontinuity,
                "Counter delta exceeded storage capacity. Establishing new baseline.", time.MonotonicSeconds);

            var gap = new UsageIntervalRecord(
                Guid.NewGuid().ToString("D"),
                $"{sessionId}_cap_exceeded_{sequence}",
                sessionId,
                clockSegmentId,
                scopeRevisionId,
                "unidentified",
                startUtcMs,
                endUtcMs,
                durationTicks,
                0, 0, true, true, "capacity_exceeded", "unreliable");
            _ = storage?.IngestGapAsync(gap);
        }
    }

    private void RecordScopeToStorage(CanonicalScopeDecision decision)
    {
        if (storage is null) return;
        var rev = new ScopeRevisionRecord(
            scopeRevisionId,
            decision.ScopeId.ToString("D"),
            decision.Revision,
            decision.Mode.ToString(),
            "RouteProvider",
            "1.0",
            string.Join(",", decision.ActiveAdapterIds),
            decision.IsAmbiguous ? "ambiguous" : "valid",
            decision.EvaluatedAt.ToUnixTimeMilliseconds());
        _ = storage.RecordScopeRevisionAsync(rev);

        foreach (var id in decision.ActiveAdapterIds)
        {
            var mem = new ScopeMembershipRecord(
                Guid.NewGuid().ToString("D"),
                scopeRevisionId,
                id,
                null,
                "unidentified",
                sessionId,
                0,
                null);
            _ = storage.RecordScopeMembershipAsync(mem);
        }
    }

    private CollectorSnapshot Publish(CollectorState state, Reading reading)
    {
        latest = new(++sequence, state, reading, observation)
        {
            ScopeDecision = scopeDecision,
            StorageStatus = storage?.Status
        };
        pendingNotification = latest;
        if (!notifying)
        {
            notifying = true;
            _ = Task.Run(NotifyLatest);
        }
        return latest;
    }

    private void NotifyLatest()
    {
        while (true)
        {
            CollectorSnapshot snapshot;
            lock (gate)
            {
                if (pendingNotification is null) { notifying = false; return; }
                snapshot = pendingNotification;
                pendingNotification = null;
            }
            var subscribers = Updated;
            if (subscribers is null) continue;
            foreach (Action<CollectorSnapshot> subscriber in subscribers.GetInvocationList())
            {
                try { subscriber(snapshot); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { Interlocked.Increment(ref subscriberFailures); }
            }
        }
    }

    private void EnsureStorageSessionInitialized(ClockReading time)
    {
        if (storage is null || sessionStartedInStorage) return;
        sessionStartedInStorage = true;

        var startMs = time.UtcNow.ToUnixTimeMilliseconds();

        var sessionRec = new SessionRecord(
            sessionId,
            time.MonotonicSeconds.ToString("F3"),
            startMs,
            null,
            null,
            false);
        _ = storage.RecordSessionStartAsync(sessionRec);

        var segmentRec = new ClockSegmentRecord(
            clockSegmentId,
            sessionId,
            (long)(time.MonotonicSeconds * 10_000_000),
            startMs,
            10_000_000,
            "nominal",
            "initial_anchor");
        _ = storage.RecordClockSegmentAsync(segmentRec);

        RecordScopeToStorage(scopeDecision);
    }

    private async Task RunAsync()
    {
        if (storage is not null && !sessionStartedInStorage)
        {
            try
            {
                var last = await storage.GetLastSessionAsync().ConfigureAwait(false);
                var now = clock.Read();
                var startMs = now.UtcNow.ToUnixTimeMilliseconds();

                lock (gate)
                {
                    EnsureStorageSessionInitialized(now);
                }

                // Unclean exit gap detection: if the previous session was not ended cleanly
                if (last is not null && last.SessionId != sessionId && !last.EndedCleanly && last.EndedAtUtcMs is null)
                {
                    var lastDurableMs = await storage.GetLastDurableTimestampUtcMsAsync(last.SessionId).ConfigureAwait(false);
                    var gapStartMs = lastDurableMs ?? last.StartedAtUtcMs;

                    if (gapStartMs < startMs)
                    {
                        var crashGap = new UsageIntervalRecord(
                            Guid.NewGuid().ToString("D"),
                            $"{sessionId}_crash_gap",
                            sessionId,
                            clockSegmentId,
                            scopeRevisionId,
                            "unidentified",
                            gapStartMs,
                            startMs,
                            null,
                            0, 0, true, true, "unclean_shutdown", "estimated");
                        await storage.IngestGapAsync(crashGap).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Storage failure does not abort collection
            }
        }

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                SampleOnce();
                await Task.Delay(TimeSpan.FromSeconds(Interval), cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellation.IsCancellationRequested) { }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public void Dispose()
    {
        Task? pending;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            cancellation.Cancel();
            pending = worker;
            Publish(CollectorState.Stopped, engine.Interrupt(clock.Read().UtcNow, ReadingState.Unavailable, "Monitoring stopped."));

            if (storage is not null)
            {
                try
                {
                    storage.RecordSessionEndAsync(sessionId, DateTimeOffset.UtcNow, true, "normal_shutdown").GetAwaiter().GetResult();
                    storage.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                }
                catch { }
            }
        }
        pending?.GetAwaiter().GetResult();
        cancellation.Dispose();
    }
}
