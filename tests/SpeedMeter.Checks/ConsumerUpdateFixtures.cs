using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.Checks;

public static class ConsumerUpdateFixtures
{
    public static void Run(Action<string, Action> check)
    {
        check("CONS-UPDATE: Opt-in default and automatic zero-request invariant", OptInDefaultNoRequests);
        check("CONS-UPDATE: Automatic cadence skips recent check", AutomaticCadenceNoRequests);
        check("CONS-UPDATE: Manual malformed manifest fails closed", ManualMalformedManifestFailsClosed);
        check("CONS-UPDATE: Semantic version precedence", SemanticVersionPrecedence);
        check("CONS-UPDATE: Release notes are signed and production key cannot be replaced by test hook", SignedNotesAndProductionAnchor);
        check("CONS-UPDATE: Offline automatic check leaves monitoring settings intact", OfflineCheck);
        check("CONS-UPDATE: Signed newer, current, and older versions order correctly", SignedVersionMatrix);
        check("CONS-UPDATE: Tampered signed manifest is rejected", TamperedServiceManifest);
        check("CONS-UPDATE: Package hash and size failures clear staging", InvalidPackageMatrix);
        check("CONS-UPDATE: Cancellation clears partial download", CancelDownload);
        check("CONS-UPDATE: Ready state survives recheck and orphan staging is cleaned on restart", ReadyStateAndRestartCleanup);
    }

    private static void OptInDefaultNoRequests()
    {
        using var fixture = new UpdateFixture();
        Require(!fixture.Settings.CurrentSettings.AutoUpdateCheckEnabled, "Background checks must default off.");
        var result = fixture.Service.CheckAsync(manual: false).GetAwaiter().GetResult();
        Require(result.State == UpdateState.Deferred && fixture.Handler.RequestCount == 0,
            "Disabled automatic check performed a request.");
    }

    private static void AutomaticCadenceNoRequests()
    {
        using var fixture = new UpdateFixture();
        var settings = fixture.Settings.CurrentSettings;
        settings.AutoUpdateCheckEnabled = true;
        settings.LastSuccessfulUpdateCheckUtc = DateTimeOffset.UtcNow.AddHours(-2);
        fixture.Settings.Save(settings);
        fixture.Service.CheckAsync(manual: false).GetAwaiter().GetResult();
        Require(fixture.Handler.RequestCount == 0, "Recent successful check did not suppress request.");
    }

    private static void ManualMalformedManifestFailsClosed()
    {
        using var fixture = new UpdateFixture();
        var result = fixture.Service.CheckAsync(manual: true).GetAwaiter().GetResult();
        Require(fixture.Handler.RequestCount == 1, "Manual check did not issue its explicit request.");
        Require(result.State == UpdateState.Failed, "Malformed manifest was not rejected.");
        Require(fixture.Settings.CurrentSettings.LastSuccessfulUpdateCheckUtc is null,
            "Failed verification must not record successful check.");
    }

    private static void SemanticVersionPrecedence()
    {
        static SemanticVersion Parse(string value)
        {
            Require(SemanticVersion.TryParse(value, out var parsed), "Invalid fixture version: " + value);
            return parsed!;
        }
        Require(Parse("1.0.0-rc.1").CompareTo(Parse("1.0.0-rc.2")) < 0, "RC order is wrong.");
        Require(Parse("1.0.0-rc.2").CompareTo(Parse("1.0.0")) < 0, "Stable must follow prerelease.");
        Require(Parse("1.0.0").CompareTo(Parse("1.0.1")) < 0, "Patch order is wrong.");
        Require(Parse("1.0.0-rc.9").CompareTo(Parse("1.0.0-rc.10")) < 0, "Numeric prerelease order is wrong.");
        Require(!SemanticVersion.TryParse("1.0.0-rc.01", out _), "Leading zero numeric label accepted.");
    }

    private static void SignedNotesAndProductionAnchor()
    {
        using var attacker = RSA.Create(2048);
        UpdateTrustAnchor.SetPinnedPublicKeyForTesting(attacker);
        try
        {
            var manifest = new UpdateManifest(1, "SpeedMeterPro", "rc", "1.0.0-rc.3",
                DateTimeOffset.UtcNow, "1.0.0-rc.2", "https://github.com/example/setup.exe",
                new string('a', 64), 1000, 3, "https://github.com/example/notes", UpdateTrustAnchor.ExpectedPublisher, "");
            var bytes = UpdateVerificationService.GetCanonicalManifestBytes(manifest);
            var signature = attacker.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            manifest = manifest with { Signature = Convert.ToBase64String(signature) };
            Require(UpdateVerificationService.VerifyManifestSignature(manifest, bytes, SigningTrustMode.DevelopmentTest),
                "Development test override unavailable to fixture.");
            Require(!UpdateVerificationService.VerifyManifestSignature(manifest, bytes, SigningTrustMode.Production),
                "Test override contaminated production trust.");
            var tampered = manifest with { ReleaseNotesUrl = "https://evil.example/notes" };
            Require(!UpdateVerificationService.VerifyManifestSignature(tampered,
                UpdateVerificationService.GetCanonicalManifestBytes(tampered), SigningTrustMode.DevelopmentTest),
                "Altered release notes URL kept its signature.");
        }
        finally { UpdateTrustAnchor.ResetPinnedKeyForTesting(); }
    }

    private static void OfflineCheck()
    {
        using var fixture = new UpdateFixture();
        fixture.Settings.CurrentSettings.AutoUpdateCheckEnabled = true;
        fixture.Settings.Save(fixture.Settings.CurrentSettings);
        fixture.Handler.ResponseFactory = (request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { RequestMessage = request });
        var result = fixture.Service.CheckAsync(manual: false).GetAwaiter().GetResult();
        Require(result.State == UpdateState.Offline && fixture.Handler.RequestCount == 1, "Offline state was not reported.");
        Require(fixture.Settings.CurrentSettings.AutoUpdateCheckEnabled, "Offline failure changed consent.");
    }

    private static void SignedVersionMatrix()
    {
        foreach (var (version, expected) in new[]
        {
            ("1.0.0-rc.3", UpdateState.Available),
            (ProductIdentity.ProductVersion, UpdateState.UpToDate),
            ("1.0.0-rc.1", UpdateState.UpToDate)
        })
        {
            using var fixture = new UpdateFixture();
            fixture.ServeSignedRelease(version);
            var result = fixture.Service.CheckAsync(manual: true).GetAwaiter().GetResult();
            Require(result.State == expected, $"{version} produced {result.State}, expected {expected}.");
            Require(fixture.Settings.CurrentSettings.LastSuccessfulUpdateCheckUtc is not null,
                "Verified manifest did not record successful check.");
        }
    }

    private static void TamperedServiceManifest()
    {
        using var fixture = new UpdateFixture();
        fixture.ServeSignedRelease("1.0.0-rc.3", tamperVersionAfterSigning: true);
        var result = fixture.Service.CheckAsync(manual: true).GetAwaiter().GetResult();
        Require(result.State == UpdateState.Failed && fixture.Settings.CurrentSettings.LastSuccessfulUpdateCheckUtc is null,
            "Tampered manifest was accepted by consumer service.");
    }

    private static void InvalidPackageMatrix()
    {
        foreach (var mismatch in new[] { "hash", "size" })
        {
            using var fixture = new UpdateFixture();
            fixture.ServeSignedRelease("1.0.0-rc.3", mismatch: mismatch);
            Require(fixture.Service.CheckAsync(manual: true).GetAwaiter().GetResult().State == UpdateState.Available,
                "Signed update was not offered before package test.");
            var result = fixture.Service.DownloadAndVerifyAsync().GetAwaiter().GetResult();
            Require(result.State == UpdateState.Failed, $"{mismatch} mismatch was accepted.");
            Require(fixture.AuthenticodeCalls == 0, $"{mismatch} mismatch reached Authenticode stage.");
            Require(Directory.GetFiles(fixture.StagingDirectory).Length == 0, $"{mismatch} mismatch left staging files.");
        }
    }

    private static void CancelDownload()
    {
        using var fixture = new UpdateFixture();
        fixture.ServeSignedRelease("1.0.0-rc.3", slowPackage: true);
        Require(fixture.Service.CheckAsync(manual: true).GetAwaiter().GetResult().State == UpdateState.Available,
            "Signed update was not offered before cancellation test.");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = fixture.Service.DownloadAndVerifyAsync(cancel.Token).GetAwaiter().GetResult();
        Require(result.State == UpdateState.Available, "Cancelled download did not return to Available.");
        Require(Directory.GetFiles(fixture.StagingDirectory).Length == 0, "Cancelled download left partial staging file.");
    }

    private static void ReadyStateAndRestartCleanup()
    {
        using var fixture = new UpdateFixture();
        fixture.ServeSignedRelease("1.0.0-rc.3");
        Require(fixture.Service.CheckAsync(manual: true).GetAwaiter().GetResult().State == UpdateState.Available,
            "Signed update was not offered.");
        var ready = fixture.Service.DownloadAndVerifyAsync().GetAwaiter().GetResult();
        Require(ready.State == UpdateState.ReadyToInstall && File.Exists(ready.ReadyInstallerPath),
            "Verified package did not reach ready staging.");
        var requestsBefore = fixture.Handler.RequestCount;
        Require(fixture.Service.CheckAsync(manual: true).GetAwaiter().GetResult().State == UpdateState.ReadyToInstall &&
                fixture.Handler.RequestCount == requestsBefore,
            "Manual recheck stranded a ready installer.");
        using var nextSession = fixture.CreateNewService();
        Require(!File.Exists(ready.ReadyInstallerPath), "Orphan ready installer was not removed on next launch.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class UpdateFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "speedmeter_update_fixture_" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient http;
        private readonly RSA signingKey = RSA.Create(2048);
        private readonly Uri manifestUri = new("https://example.test/updates/rc.json");
        public SettingsService Settings { get; }
        public CountingHandler Handler { get; } = new();
        public UpdateService Service { get; }
        public string StagingDirectory => Path.Combine(directory, "staging");
        public int AuthenticodeCalls { get; private set; }

        public UpdateFixture()
        {
            Directory.CreateDirectory(directory);
            UpdateTrustAnchor.SetPinnedPublicKeyForTesting(signingKey);
            Settings = new SettingsService(Path.Combine(directory, "settings.json"));
            http = new HttpClient(Handler);
            Service = CreateNewService();
        }

        public UpdateService CreateNewService() => new(Settings, http, StagingDirectory, null,
            manifestUri, SigningTrustMode.DevelopmentTest, (_, _, _) => AuthenticodeCalls++);

        public void ServeSignedRelease(string version, string? mismatch = null, bool tamperVersionAfterSigning = false,
            bool slowPackage = false)
        {
            var package = new byte[1024];
            Random.Shared.NextBytes(package);
            var hash = Convert.ToHexStringLower(SHA256.HashData(package));
            if (mismatch == "hash") hash = new string('0', 64);
            var size = package.LongLength + (mismatch == "size" ? 1 : 0);
            var manifest = new UpdateManifest(1, "SpeedMeterPro", "rc", version, DateTimeOffset.UtcNow,
                "1.0.0-rc.1",
                $"https://github.com/abdullahalmamun-devv/NetSpeedMeter-Pro/releases/download/v{version}/SpeedMeterPro-{version}-Setup.exe",
                hash, size, 3, "https://github.com/abdullahalmamun-devv/NetSpeedMeter-Pro/releases",
                UpdateTrustAnchor.ExpectedPublisher, "");
            var bytes = UpdateVerificationService.GetCanonicalManifestBytes(manifest);
            manifest = manifest with { Signature = Convert.ToBase64String(signingKey.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) };
            if (tamperVersionAfterSigning) manifest = manifest with { Version = "1.0.0-rc.4" };
            var json = JsonSerializer.Serialize(manifest);
            Handler.ResponseFactory = (request, _) =>
            {
                HttpContent content = request.RequestUri == manifestUri
                    ? new StringContent(json)
                    : slowPackage ? new StreamContent(new SlowCancelableStream(package)) : new ByteArrayContent(package);
                if (request.RequestUri != manifestUri && slowPackage)
                    content.Headers.ContentLength = package.LongLength;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { RequestMessage = request, Content = content });
            };
        }

        public void Dispose()
        {
            Service.Dispose();
            http.Dispose();
            UpdateTrustAnchor.ResetPinnedKeyForTesting();
            signingKey.Dispose();
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? ResponseFactory { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (ResponseFactory is not null) return ResponseFactory(request, cancellationToken);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("{\"schemaVersion\":999}")
            });
        }
    }

    private sealed class SlowCancelableStream : Stream
    {
        private readonly byte[] firstChunk;
        private bool sent;
        public SlowCancelableStream(byte[] firstChunk) => this.firstChunk = firstChunk;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!sent)
            {
                sent = true;
                var count = Math.Min(buffer.Length, Math.Min(firstChunk.Length, 128));
                firstChunk.AsMemory(0, count).CopyTo(buffer);
                return count;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
