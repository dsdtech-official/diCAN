using System.Runtime.InteropServices;
using DiCAN.Core.Abstractions;

namespace DiCAN.Windows.Interop;

// Manages timer resolution.
public sealed class TimerResolution : ITimerResolution
{
    private const uint OneMillisecond = 1;

    // Acquires the shared resource.
    public IDisposable Acquire() =>
        OperatingSystem.IsWindows() && TryBegin() ? new Holder() : NullTimerResolution.Instance.Acquire();

    // Tries begin.
    private static bool TryBegin()
    {

        try
        {
            return NativeMethods.TimeBeginPeriod(OneMillisecond) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    // Manages holder.
    private sealed class Holder : IDisposable
    {
        private bool _released;

        // Releases held resources.
        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            try
            {
                NativeMethods.TimeEndPeriod(OneMillisecond);
            }
            catch (DllNotFoundException)
            {
            }
        }
    }

    // Manages native methods.
    private static class NativeMethods
    {

        // Gets time begin period.
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        internal static extern uint TimeBeginPeriod(uint milliseconds);

        // Gets time end period.
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        internal static extern uint TimeEndPeriod(uint milliseconds);
    }
}
