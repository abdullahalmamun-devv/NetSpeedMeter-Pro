using System;
using System.Collections.Generic;
using System.Windows.Input;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.App.ViewModels;

public sealed class LiveTrafficViewModel : ViewModelBase
{
    private readonly ILiveGraphService graphService;
    private readonly Collector collector;

    private TimeSpan selectedWindow = TimeSpan.FromSeconds(60);
    private string windowLabel = "1 Minute";
    private string downloadRateText = "—";
    private string uploadRateText = "—";
    private string combinedRateText = "—";
    private string sessionDurationText = "00:00:00";
    private string sessionRxText = "0 B";
    private string sessionTxText = "0 B";
    private string sessionTotalText = "0 B";
    private string sessionAvgText = "0 B/s";
    private string sessionPeakText = "0 B/s";
    private string accessibilitySummary = "Live traffic graph: idle.";
    private bool isBits;

    private long sessionStartUtcMs;
    private double sessionPeakRate;
    private long lastSequence;

    public TimeSpan SelectedWindow { get => selectedWindow; private set => SetProperty(ref selectedWindow, value); }
    public string WindowLabel { get => windowLabel; private set => SetProperty(ref windowLabel, value); }
    public string DownloadRateText { get => downloadRateText; private set => SetProperty(ref downloadRateText, value); }
    public string UploadRateText { get => uploadRateText; private set => SetProperty(ref uploadRateText, value); }
    public string CombinedRateText { get => combinedRateText; private set => SetProperty(ref combinedRateText, value); }
    public string SessionDurationText { get => sessionDurationText; private set => SetProperty(ref sessionDurationText, value); }
    public string SessionRxText { get => sessionRxText; private set => SetProperty(ref sessionRxText, value); }
    public string SessionTxText { get => sessionTxText; private set => SetProperty(ref sessionTxText, value); }
    public string SessionTotalText { get => sessionTotalText; private set => SetProperty(ref sessionTotalText, value); }
    public string SessionAvgText { get => sessionAvgText; private set => SetProperty(ref sessionAvgText, value); }
    public string SessionPeakText { get => sessionPeakText; private set => SetProperty(ref sessionPeakText, value); }
    public string AccessibilitySummary { get => accessibilitySummary; private set => SetProperty(ref accessibilitySummary, value); }
    public bool IsBits { get => isBits; set { if (SetProperty(ref isBits, value)) RefreshDisplay(); } }

    public ICommand SelectWindowCommand { get; }
    public ICommand ResetSessionCommand { get; }

    public LiveTrafficViewModel(ILiveGraphService graphService, Collector collector)
    {
        this.graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
        this.collector = collector ?? throw new ArgumentNullException(nameof(collector));

        sessionStartUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        SelectWindowCommand = new RelayCommand(p =>
        {
            if (p is string s && int.TryParse(s, out var seconds))
            {
                SetWindowSeconds(seconds);
            }
        });

        ResetSessionCommand = new RelayCommand(() =>
        {
            collector.Reset();
            sessionStartUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            sessionPeakRate = 0;
            RefreshDisplay();
        });
    }

    public void SetWindowSeconds(int seconds)
    {
        SelectedWindow = TimeSpan.FromSeconds(seconds);
        WindowLabel = seconds switch
        {
            30 => "30 Seconds",
            60 => "1 Minute",
            300 => "5 Minutes",
            900 => "15 Minutes",
            3600 => "1 Hour",
            _ => $"{seconds}s"
        };
        RefreshDisplay();
    }

    public void UpdateSnapshot(CollectorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lastSequence = snapshot.Sequence;

        var reading = snapshot.Reading;
        if (reading.State == ReadingState.Live)
        {
            var down = reading.Download ?? 0;
            var up = reading.Upload ?? 0;
            var total = down + up;
            if (total > sessionPeakRate) sessionPeakRate = total;
        }

        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        var snapshot = collector.Latest;
        var reading = snapshot.Reading;

        if (reading.State == ReadingState.Live)
        {
            DownloadRateText = MetricFormat.Rate(reading.Download, isBits);
            UploadRateText = MetricFormat.Rate(reading.Upload, isBits);
            var combined = (reading.Download ?? 0) + (reading.Upload ?? 0);
            CombinedRateText = MetricFormat.Rate(combined, isBits);
        }
        else
        {
            DownloadRateText = "—";
            UploadRateText = "—";
            CombinedRateText = "—";
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var elapsedSeconds = Math.Max(1.0, (nowMs - sessionStartUtcMs) / 1000.0);
        var ts = TimeSpan.FromSeconds(elapsedSeconds);
        SessionDurationText = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";

        SessionRxText = MetricFormat.Bytes(reading.Received);
        SessionTxText = MetricFormat.Bytes(reading.Sent);
        var totalBytes = reading.Received + reading.Sent;
        SessionTotalText = MetricFormat.Bytes(totalBytes);

        var avgRate = totalBytes / elapsedSeconds;
        SessionAvgText = MetricFormat.Rate(avgRate, isBits);
        SessionPeakText = MetricFormat.Rate(sessionPeakRate, isBits);

        AccessibilitySummary = $"Window {WindowLabel}. Download: {DownloadRateText}, Upload: {UploadRateText}. Session: {SessionTotalText} in {SessionDurationText}.";
    }

    public IReadOnlyList<LiveGraphPoint> GetCurrentWindowPoints()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return graphService.GetRawPoints(selectedWindow, nowMs);
    }
}
