using System;
using SpeedMeter.Core;

namespace SpeedMeter.Application;

public interface ISettingsService
{
    SpeedMeterSettings CurrentSettings { get; }
    string? LastWarning { get; }

    SpeedMeterSettings Load();
    void Save(SpeedMeterSettings settings);

    event Action<SpeedMeterSettings>? SettingsChanged;
}

public sealed class SettingsService : ISettingsService
{
    private readonly string settingsFilePath;
    private readonly object gate = new();
    private SpeedMeterSettings current;
    private string? lastWarning;

    public SpeedMeterSettings CurrentSettings
    {
        get
        {
            lock (gate) return current;
        }
    }

    public string? LastWarning
    {
        get
        {
            lock (gate) return lastWarning;
        }
    }

    public event Action<SpeedMeterSettings>? SettingsChanged;

    public SettingsService(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        this.settingsFilePath = settingsFilePath;
        var (loaded, warn) = SettingsStorage.Load(settingsFilePath);
        this.current = loaded;
        this.lastWarning = warn;
    }

    public SpeedMeterSettings Load()
    {
        lock (gate)
        {
            var (loaded, warn) = SettingsStorage.Load(settingsFilePath);
            this.current = loaded;
            this.lastWarning = warn;
            return loaded;
        }
    }

    public void Save(SpeedMeterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (gate)
        {
            SettingsStorage.SaveAtomic(settingsFilePath, settings);
            this.current = settings;
        }
        SettingsChanged?.Invoke(settings);
    }
}
