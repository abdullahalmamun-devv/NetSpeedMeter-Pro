using System;
using System.Collections.Generic;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Windows;

namespace SpeedMeter.App;

internal sealed class MonitorSession : IDisposable
{
    private readonly Collector collector;

    public CollectorSnapshot Latest => collector.Latest;

    public MonitorSession()
    {
        collector = new Collector(new AdapterProvider(), new MeasurementClock());
    }

    public double Interval
    {
        get => collector.Interval;
        set => collector.Interval = value;
    }

    public bool Paused => collector.Paused;

    public void Start() => collector.Start();

    public void Select(string? id) => collector.Select(id);

    public void Reset() => collector.Reset();

    public void TogglePause() => collector.TogglePause();

    public void SetSuspended(bool value) => collector.SetSuspended(value);

    public void Dispose()
    {
        collector.Dispose();
    }
}
