namespace DiCAN.Core.Abstractions;

// Manages i monotonic.
public interface IMonotonicClock
{

    DateTimeOffset Now { get; }

    TimeSpan Resolution { get; }
}
