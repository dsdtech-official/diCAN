using DiCAN.Core.Devices;
using DiCAN.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DiCAN.Infrastructure.Devices;

// Monitors connected devices.
public sealed class PollingCanDeviceWatcher(
    ICanDeviceEnumerator enumerator,
    ILogger? logger = null,
    TimeSpan? pollInterval = null)
    : ICanDeviceWatcher
{

    private readonly ILogger _logger =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(1500);

    private readonly TimeSpan _pollInterval = pollInterval ?? DefaultPollInterval;

    private readonly Lock _sync = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IReadOnlyList<CanDeviceInfo> _lastSnapshot = [];

    private bool _starting;

    private volatile bool _isRunning;

    public bool IsRunning { get => _isRunning; private set => _isRunning = value; }

    public event EventHandler<CanDevicesChangedEventArgs>? DevicesChanged;

    // Starts the operation.
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isRunning || _starting)
            {
                return;
            }

            _starting = true;
        }

        try
        {

            cancellationToken.ThrowIfCancellationRequested();

            bool baselineKnown = true;

            try
            {
                _lastSnapshot = enumerator.Enumerate();
            }
            catch (OperationCanceledException)
            {

                throw;
            }
            catch (Exception e)
            {
                baselineKnown = false;
                _lastSnapshot = [];
                CanDeviceWatcherLog.BaselineFailed(_logger, e, e.GetType().Name);
            }

            if (baselineKnown)
            {
                CanDeviceWatcherLog.Started(
                    _logger, _lastSnapshot.Count, CanDeviceWatcherLog.Describe(_lastSnapshot));
            }

            var cts = new CancellationTokenSource();
            Task loop = Task.Run(() => PollLoopAsync(cts.Token, baselineKnown), CancellationToken.None);

            lock (_sync)
            {
                _cts = cts;
                _loop = loop;
            }

            IsRunning = true;
        }
        finally
        {
            lock (_sync)
            {
                _starting = false;
            }
        }

        await Task.CompletedTask;
    }

    // Stops the active operation.
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (_sync)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        if (cts is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts.Dispose();
        IsRunning = false;

        CanDeviceWatcherLog.Stopped(_logger);
    }

    // Releases held resources.
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        DevicesChanged = null;
    }

    // Polls loop.
    private async Task PollLoopAsync(CancellationToken token, bool baselineKnown = true)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        bool needsBaseline = !baselineKnown;

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                IReadOnlyList<CanDeviceInfo> current;

                try
                {
                    current = enumerator.Enumerate();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {

                    _logger.LogDebug(
                        e, "CAN device enumeration failed for this poll; skipping the round.");
                    continue;
                }

                if (needsBaseline)
                {
                    needsBaseline = false;

                    lock (_sync)
                    {
                        _lastSnapshot = current;
                    }

                    CanDeviceWatcherLog.BaselineRecovered(
                        _logger, current.Count, CanDeviceWatcherLog.Describe(current));
                    continue;
                }

                Compare(
                    current,
                    out IReadOnlyList<CanDeviceInfo> added,
                    out IReadOnlyList<CanDeviceInfo> removed,
                    out bool portsAppeared);

                if (added.Count == 0 && removed.Count == 0 && !portsAppeared)
                {

                    CanDeviceWatcherLog.PolledNoChange(_logger, current.Count);
                    continue;
                }

                CanDeviceWatcherLog.DevicesChanged(
                    _logger, added.Count, removed.Count, current.Count,
                    CanDeviceWatcherLog.Describe(added),
                    CanDeviceWatcherLog.Describe(removed),
                    CanDeviceWatcherLog.Describe(current));

                try
                {
                    DevicesChanged?.Invoke(this, new CanDevicesChangedEventArgs(added, removed, current));
                }
                catch (Exception e)
                {
                    CanDeviceWatcherLog.SubscriberFailed(_logger, e, e.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {

            CanDeviceWatcherLog.Faulted(_logger, e, e.GetType().Name);
            IsRunning = false;
        }
    }

    // Compares the requested values.
    private void Compare(
        IReadOnlyList<CanDeviceInfo> current,
        out IReadOnlyList<CanDeviceInfo> added,
        out IReadOnlyList<CanDeviceInfo> removed,
        out bool portsAppeared)
    {
        lock (_sync)
        {
            HashSet<string> previousIds = _lastSnapshot
                .Select(d => d.DeviceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            HashSet<string> currentIds = current
                .Select(d => d.DeviceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            added = current.Where(d => !previousIds.Contains(d.DeviceId)).ToArray();
            removed = _lastSnapshot.Where(d => !currentIds.Contains(d.DeviceId)).ToArray();

            portsAppeared = PortsAppeared(_lastSnapshot, current);

            _lastSnapshot = current;
        }
    }

    // Gets ports appeared.
    private static bool PortsAppeared(
        IReadOnlyList<CanDeviceInfo> previous, IReadOnlyList<CanDeviceInfo> current)
    {
        Dictionary<string, CanDeviceInfo> before = previous
            .GroupBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return current.Any(now =>
            now.PortName is not null &&
            before.TryGetValue(now.DeviceId, out CanDeviceInfo? was) &&
            was.PortName is null);
    }
}
