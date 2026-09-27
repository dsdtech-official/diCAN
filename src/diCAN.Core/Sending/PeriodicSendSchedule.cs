using DiCAN.Core.Protocol;

namespace DiCAN.Core.Sending;

// Schedules periodic CAN frames.
public sealed record PeriodicSendTask(CanFrame Frame, TimeSpan Interval, int? RepeatCount = null);

// Schedules periodic CAN frames.
public readonly record struct PeriodicSendEntry(
    int Handle,
    PeriodicSendTask Task,
    int Sent,
    DateTimeOffset NextDueAt,
    bool Enabled = true,
    bool HasBeenEnabled = true)
{

    public int? Remaining => Task.RepeatCount is { } total ? total - Sent : null;

    public bool IsPaused => !Enabled && HasBeenEnabled;
}

// Schedules periodic CAN frames.
public readonly record struct PeriodicSendDispatch(int Handle, CanFrame Frame);

// Schedules periodic CAN frames.
public readonly record struct PeriodicSendBatch(
    IReadOnlyList<PeriodicSendDispatch> Due, IReadOnlyList<int> Completed, int SkippedOccurrences)
{

    public bool IsEmpty => Due.Count == 0 && Completed.Count == 0;
}

// Schedules periodic CAN frames.
public sealed class PeriodicSendSchedule
{

    // Manages slot.
    private sealed class Slot
    {
        public required int Handle;
        public required PeriodicSendTask Task;
        public int Sent;
        public DateTimeOffset NextDueAt;

        public bool Enabled = true;

        public bool HasBeenEnabled;
    }

    private readonly List<Slot> _slots = [];
    private int _nextHandle = 1;

    public int Count => _slots.Count;

    public bool IsIdle => !_slots.Any(s => s.Enabled);

    public DateTimeOffset? NextDueAt =>
        _slots.Where(s => s.Enabled).Select(s => (DateTimeOffset?)s.NextDueAt).Min();

    public IReadOnlyList<PeriodicSendEntry> Entries =>
    [
        .. _slots
            .OrderBy(s => s.Handle)
            .Select(s => new PeriodicSendEntry(
                s.Handle, s.Task, s.Sent, s.NextDueAt, s.Enabled, s.HasBeenEnabled)),
    ];

    // Sets enabled.
    public bool SetEnabled(int handle, bool enabled, DateTimeOffset now)
    {
        if (_slots.FirstOrDefault(s => s.Handle == handle) is not { } slot)
        {
            return false;
        }

        if (slot.Enabled == enabled)
        {
            return true;
        }

        slot.Enabled = enabled;

        if (enabled)
        {

            slot.NextDueAt = now + slot.Task.Interval;

            slot.HasBeenEnabled = true;

            RestartIfFinished(slot);
        }

        return true;
    }

    // Restarts the active operation.
    private static void RestartIfFinished(Slot slot)
    {
        if (slot.Task.RepeatCount is { } total && slot.Sent >= total)
        {
            slot.Sent = 0;
        }
    }

    // Adds the requested entry.
    public int Add(PeriodicSendTask task, DateTimeOffset now, bool enabled = true)
    {
        if (task.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(task),
                task.Interval,
                "A periodic send interval must be greater than zero.");
        }

        if (task.RepeatCount is { } count && count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(task),
                count,
                "A repeat count must be at least 1. Use null to repeat until stopped.");
        }

        if (!task.Frame.IsValid)
        {
            throw new ArgumentException(
                "The frame is not a combination a CAN device can produce.", nameof(task));
        }

        int handle = _nextHandle++;

        _slots.Add(new Slot
        {
            Handle = handle,
            Task = task,
            NextDueAt = now + task.Interval,

            Enabled = enabled,

            HasBeenEnabled = enabled,
        });

        return handle;
    }

    // Removes the requested entry.
    public bool Remove(int handle) => _slots.RemoveAll(s => s.Handle == handle) > 0;

    // Updates the current state.
    public bool Update(int handle, PeriodicSendTask task, DateTimeOffset now)
    {
        if (task.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(task),
                task.Interval,
                "A periodic send interval must be greater than zero.");
        }

        if (task.RepeatCount is { } count && count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(task),
                count,
                "A repeat count must be at least 1. Use null to repeat until stopped.");
        }

        if (!task.Frame.IsValid)
        {
            throw new ArgumentException(
                "The frame is not a combination a CAN device can produce.", nameof(task));
        }

        if (_slots.FirstOrDefault(s => s.Handle == handle) is not { } slot)
        {
            return false;
        }

        if (task.RepeatCount is { } total && total <= slot.Sent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(task),
                total,
                $"This row has already sent {slot.Sent} frame(s). "
                + "Choose a higher repeat count, or reset the counters first.");
        }

        bool intervalMoved = task.Interval != slot.Task.Interval;

        slot.Task = task;

        if (intervalMoved)
        {
            slot.NextDueAt = now + task.Interval;
        }

        return true;
    }

    // Clears the stored state.
    public void Clear() => _slots.Clear();

    // Resets counters.
    public void ResetCounters()
    {
        foreach (Slot slot in _slots)
        {
            slot.Sent = 0;
        }
    }

    // Resets counter.
    public bool ResetCounter(int handle)
    {
        if (_slots.FirstOrDefault(s => s.Handle == handle) is not { } slot)
        {
            return false;
        }

        slot.Sent = 0;

        return true;
    }

    // Pauses active transmissions.
    public int PauseAll()
    {
        int paused = 0;

        foreach (Slot slot in _slots.Where(s => s.Enabled))
        {
            slot.Enabled = false;
            paused++;
        }

        return paused;
    }

    // Resumes paused transmissions.
    public int ResumeAll(DateTimeOffset now)
    {
        int resumed = 0;

        foreach (Slot slot in _slots.Where(s => !s.Enabled && s.HasBeenEnabled))
        {
            slot.Enabled = true;
            slot.NextDueAt = now + slot.Task.Interval;

            RestartIfFinished(slot);

            resumed++;
        }

        return resumed;
    }

    // Gets the next available item.
    public PeriodicSendBatch TakeDue(DateTimeOffset now)
    {
        List<PeriodicSendDispatch>? due = null;
        List<int>? completed = null;
        int skipped = 0;

        foreach (Slot slot in _slots.OrderBy(s => s.Handle))
        {

            if (!slot.Enabled || slot.NextDueAt > now)
            {
                continue;
            }

            (due ??= []).Add(new PeriodicSendDispatch(slot.Handle, slot.Task.Frame));
            slot.Sent++;

            if (slot.Task.RepeatCount is { } total && slot.Sent >= total)
            {
                (completed ??= []).Add(slot.Handle);
                continue;
            }

            skipped += AdvancePastNow(slot, now);
        }

        if (completed is not null)
        {

            foreach (Slot slot in _slots.Where(s => completed.Contains(s.Handle)))
            {
                slot.Enabled = false;
            }
        }

        return new PeriodicSendBatch(
            (IReadOnlyList<PeriodicSendDispatch>?)due ?? [],
            (IReadOnlyList<int>?)completed ?? [],
            skipped);
    }

    // Gets advance past now.
    private static int AdvancePastNow(Slot slot, DateTimeOffset now)
    {
        slot.NextDueAt += slot.Task.Interval;

        if (slot.NextDueAt > now)
        {
            return 0;
        }

        long missed = (long)((now - slot.NextDueAt).Ticks / slot.Task.Interval.Ticks) + 1;
        slot.NextDueAt += TimeSpan.FromTicks(missed * slot.Task.Interval.Ticks);

        return (int)Math.Min(missed, int.MaxValue);
    }
}
