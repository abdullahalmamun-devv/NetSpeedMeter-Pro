using System;
using System.Collections.Generic;
using SpeedMeter.Core;

namespace SpeedMeter.Application;

public interface ILiveGraphService
{
    void RecordSample(long timestampUtcMs, double? downloadRate, double? uploadRate, bool isValid, bool isGap, long sequence);
    IReadOnlyList<LiveGraphPoint> GetRawPoints(TimeSpan window, long nowUtcMs);
    IReadOnlyList<DownsampledPoint> GetDownsampled(TimeSpan window, int bucketCount, long nowUtcMs);
}

public sealed class LiveGraphService : ILiveGraphService
{
    private readonly LiveGraphBuffer buffer;

    public LiveGraphService(int capacitySeconds = 3600)
    {
        this.buffer = new LiveGraphBuffer(capacitySeconds);
    }

    public void RecordSample(long timestampUtcMs, double? downloadRate, double? uploadRate, bool isValid, bool isGap, long sequence)
    {
        buffer.Append(new LiveGraphPoint(timestampUtcMs, downloadRate, uploadRate, isValid, isGap, sequence));
    }

    public IReadOnlyList<LiveGraphPoint> GetRawPoints(TimeSpan window, long nowUtcMs)
    {
        return buffer.GetPoints(window, nowUtcMs);
    }

    public IReadOnlyList<DownsampledPoint> GetDownsampled(TimeSpan window, int bucketCount, long nowUtcMs)
    {
        return buffer.GetDownsampled(window, bucketCount, nowUtcMs);
    }
}
