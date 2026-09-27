namespace DiCAN.Core.Protocol;

// Defines can bus status values.
public enum CanBusStatus
{

    Active,

    Warning,

    ErrorPassive,

    BusOff,
}

// Manages can bus status text.
public static class CanBusStatusText
{

    // Gets phrase.
    public static string Phrase(CanBusStatus status) => status switch
    {
        CanBusStatus.Warning => "is degraded (error counters above 96)",
        CanBusStatus.ErrorPassive =>
            "is error-passive (above 128): the adapter no longer signals errors actively",
        CanBusStatus.BusOff =>
            "is bus-off (above 248): the adapter has left the bus and is not transmitting",
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "Active has no degraded phrase."),
    };
}
