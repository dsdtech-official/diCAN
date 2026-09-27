namespace DiCAN.Core.Abstractions;

// Manages i timer.
public interface ITimerResolution
{

    // Acquires the shared resource.
    IDisposable Acquire();
}

// Manages null timer resolution.
public sealed class NullTimerResolution : ITimerResolution
{
    public static NullTimerResolution Instance { get; } = new();

    private static readonly IDisposable Handle = new NoOp();

    // Acquires the shared resource.
    public IDisposable Acquire() => Handle;

    // Manages no op.
    private sealed class NoOp : IDisposable
    {

        // Releases held resources.
        public void Dispose()
        {
        }
    }
}
