using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using SpeedMeter.Core;

namespace SpeedMeter.App;

public sealed class TrafficChart : FrameworkElement
{
    private readonly List<(double Time, Reading Value)> readings = [];
    public bool Bits { get; set; }

    public void Add(Reading reading)
    {
        if (reading.MonotonicSeconds is not { } now || !double.IsFinite(now))
        { Clear(); return; }
        if (readings.Count > 0 && (reading.SessionId != readings[^1].Value.SessionId ||
            reading.MeasurementEpoch != readings[^1].Value.MeasurementEpoch || now <= readings[^1].Time)) Clear();
        readings.Add((now, reading));
        readings.RemoveAll(r => r.Time < now - 60);
        if (IsVisible) InvalidateVisual();
    }

    public void Clear() { readings.Clear(); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 10 || height < 10) return;
        const double labelHeight = 25;
        var plotHeight = height - labelHeight;
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(43, 59, 80)), 1);
        for (var i = 0; i <= 3; i++)
            dc.DrawLine(gridPen, new Point(0, labelHeight + plotHeight * i / 3), new Point(width, labelHeight + plotHeight * i / 3));
        var valid = readings.Where(r => r.Value.IsValid).ToArray();
        var peak = valid.Length == 0 ? 1 : Math.Max(1, valid.Max(r => Math.Max(r.Value.Download ?? 0, r.Value.Upload ?? 0)));
        var label = new FormattedText("Scale 0–" + MetricFormat.Rate(peak, Bits) + "   ·   ↓ Download    ↑ Upload (dashed)",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11,
            new SolidColorBrush(Color.FromRgb(168, 182, 202)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point(0, 0));
        var latest = readings.Count == 0 ? 0 : readings[^1].Time;
        for (var direction = 0; direction < 2; direction++)
        {
            Point? previous = null;
            var pen = new Pen(new SolidColorBrush(direction == 0 ? Color.FromRgb(121, 185, 255) : Color.FromRgb(114, 219, 200)), 2);
            if (direction == 1) pen.DashStyle = DashStyles.Dash;
            foreach (var (time, value) in readings)
            {
                // Coarse intervals retain accounting but must not invent a short-term graph peak.
                if (!value.IsValid || value.Coarse) { previous = null; continue; }
                var rate = direction == 0 ? value.Download!.Value : value.Upload!.Value;
                var point = new Point(width * (1 - (latest - time) / 60), labelHeight + plotHeight * (1 - rate / peak));
                if (previous is { } p) dc.DrawLine(pen, p, point);
                else dc.DrawEllipse(pen.Brush, null, point, 2, 2);
                previous = point;
            }
        }
    }
}
