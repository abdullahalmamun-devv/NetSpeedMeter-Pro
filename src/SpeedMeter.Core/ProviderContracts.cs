namespace SpeedMeter.Core;

public enum ProviderAvailability
{
    Available, Partial, Unsupported, PermissionDenied, TemporarilyUnavailable, Failed
}

/// <summary>One immutable observation from one named measurement method.</summary>
public sealed class AdapterObservation
{
    public ProviderAvailability Availability { get; }
    public IReadOnlyList<AdapterCounter> Adapters { get; }
    public string Method { get; }
    public string Code { get; }
    public string Detail { get; }

    public AdapterObservation(ProviderAvailability availability, IEnumerable<AdapterCounter> adapters,
        string method, string code, string detail)
    {
        Availability = availability;
        Adapters = Array.AsReadOnly(adapters.ToArray());
        Method = method;
        Code = code;
        Detail = detail;
    }
}

public interface IAdapterCounterProvider
{
    AdapterObservation ReadSnapshot();
}
