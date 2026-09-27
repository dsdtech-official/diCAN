using System.Globalization;

namespace DiCAN.Core.Diagnostics;

// Stores throughput sample data.
public sealed record ThroughputSample
{

    public required string Transport { get; init; }

    public required int PayloadBytes { get; init; }

    public required int OfferedRate { get; init; }

    public int LadderPosition => OfferedRate <= 0 ? int.MaxValue : OfferedRate;

    public string RungLabel => OfferedRate <= 0
        ? "unpaced"
        : OfferedRate.ToString(CultureInfo.InvariantCulture);

    public required long Sent { get; init; }

    public required long Received { get; init; }

    public required double Seconds { get; init; }

    public long HostOverflows { get; init; }

    public bool TxEchoEnabled { get; init; }

    public bool HasData => Sent > 0 && Seconds > 0;

    public double LossPercent => Sent <= 0 ? 0 : (Sent - Received) * 100.0 / Sent;

    public bool IsImplausible => HasData && Received > Sent;

    public double ReceivedPerSecond => Seconds <= 0 ? 0 : Received / Seconds;

    public double PayloadKbPerSecond => ReceivedPerSecond * PayloadBytes / 1024.0;

    // Converts the requested value.
    public string ToLine() => string.Create(CultureInfo.InvariantCulture,
        $"MEAS transport={Transport} payload={PayloadBytes} offered={OfferedRate} " +
        $"tx={Sent} rx={Received} secs={Seconds:F2} loss={LossPercent:F2} " +
        $"rxps={ReceivedPerSecond:F0} kbps={PayloadKbPerSecond:F1} " +
        $"overflows={HostOverflows} txecho={(TxEchoEnabled ? "ON" : "off")}");
}

// Manages throughput ladder.
public enum ThroughputLadderOutcome
{

    NoData,

    NeverBreached,

    Breached,

    Inconsistent,
}

// Manages throughput verdict.
public sealed record ThroughputVerdict(
    ThroughputLadderOutcome Outcome,
    ThroughputSample? CleanCeiling,
    ThroughputSample? FirstBreach,
    IReadOnlyList<string> Warnings)
{

    public bool IsUsable => Outcome == ThroughputLadderOutcome.Breached && Warnings.Count == 0;
}

// Manages throughput ladder.
public static class ThroughputLadder
{

    public const double DefaultLossThresholdPercent = 1.0;

    // Tries parse line.
    public static bool TryParseLine(string? line, out ThroughputSample? sample)
    {
        sample = null;

        if (line is null)
        {
            return false;
        }

        string trimmed = line.Trim();
        if (!trimmed.StartsWith("MEAS ", StringComparison.Ordinal))
        {
            return false;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string token in trimmed[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = token.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                fields[token[..equals]] = token[(equals + 1)..];
            }
        }

        if (!fields.TryGetValue("transport", out string? transport) ||
            !TryLong(fields, "tx", out long tx) ||
            !TryLong(fields, "rx", out long rx) ||
            !TryLong(fields, "payload", out long payload) ||
            !TryLong(fields, "offered", out long offered) ||
            !TryDouble(fields, "secs", out double secs))
        {
            return false;
        }

        TryLong(fields, "overflows", out long overflows);

        sample = new ThroughputSample
        {
            Transport = transport,
            PayloadBytes = (int)payload,
            OfferedRate = (int)offered,
            Sent = tx,
            Received = rx,
            Seconds = secs,
            HostOverflows = overflows,
            TxEchoEnabled = fields.TryGetValue("txecho", out string? echo)
                && echo.Equals("ON", StringComparison.OrdinalIgnoreCase),
        };

        return true;

        // Tries long.
        static bool TryLong(Dictionary<string, string> f, string key, out long value) =>
            long.TryParse(
                f.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        // Tries double.
        static bool TryDouble(Dictionary<string, string> f, string key, out double value) =>
            double.TryParse(
                f.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // Merges the requested data.
    public static IReadOnlyList<ThroughputSample> Merge(IEnumerable<ThroughputSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        return [.. samples
            .GroupBy(s => (s.Transport, s.PayloadBytes, s.OfferedRate))
            .Select(g => new ThroughputSample
            {
                Transport = g.Key.Transport,
                PayloadBytes = g.Key.PayloadBytes,
                OfferedRate = g.Key.OfferedRate,
                Sent = g.Max(s => s.Sent),
                Received = g.Max(s => s.Received),
                Seconds = g.Max(s => s.Seconds),
                HostOverflows = g.Max(s => s.HostOverflows),
                TxEchoEnabled = g.Any(s => s.TxEchoEnabled),
            })
            .OrderBy(s => s.LadderPosition)];
    }

    // Checks operation status.
    public static ThroughputVerdict Judge(
        IEnumerable<ThroughputSample> samples,
        double lossThresholdPercent = DefaultLossThresholdPercent)
    {
        ArgumentNullException.ThrowIfNull(samples);

        List<ThroughputSample> all = [.. samples];
        var warnings = new List<string>();

        if (all.Select(s => s.Transport).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            return new ThroughputVerdict(
                ThroughputLadderOutcome.NoData, null, null,
                ["Ladder mixes transports. One ladder measures one transport."]);
        }

        if (all.Select(s => s.PayloadBytes).Distinct().Count() > 1)
        {
            return new ThroughputVerdict(
                ThroughputLadderOutcome.NoData, null, null,
                ["Ladder mixes payload sizes. Frames per second are not comparable across them."]);
        }

        List<ThroughputSample> usable = [.. all.Where(s => s.HasData).OrderBy(s => s.LadderPosition)];

        int empty = all.Count - usable.Count;
        if (empty > 0)
        {
            warnings.Add($"{empty} rung(s) recorded no frames at all and were ignored.");
        }

        if (usable.Count == 0)
        {
            return new ThroughputVerdict(
                ThroughputLadderOutcome.NoData, null, null, [.. warnings, "No rung carried data."]);
        }

        if (usable.Any(s => s.TxEchoEnabled))
        {
            warnings.Add(
                "Transmit echo was ON. The retest plan voids such a run: an undrained echo "
                + "stream starves the device frame pool and the reading is from a degraded device.");
        }

        foreach (ThroughputSample s in usable.Where(s => s.IsImplausible))
        {
            warnings.Add(
                $"Rung {s.RungLabel} received more than it sent ({s.Received} > {s.Sent}). "
                + "The two counters are not counting the same thing.");
        }

        ThroughputSample? firstBreach =
            usable.FirstOrDefault(s => s.LossPercent > lossThresholdPercent);

        if (firstBreach is null)
        {

            ThroughputSample top = usable[^1];
            warnings.Add(
                $"Loss never exceeded {lossThresholdPercent:F1}%. "
                + $"{top.ReceivedPerSecond:F0} frames/s is a LOWER BOUND, not a ceiling.");

            return new ThroughputVerdict(
                ThroughputLadderOutcome.NeverBreached, top, null, warnings);
        }

        ThroughputSample? ceiling = usable
            .Where(s => s.LadderPosition < firstBreach.LadderPosition
                        && s.LossPercent <= lossThresholdPercent)
            .LastOrDefault();

        List<ThroughputSample> cleanAbove = [.. usable
            .Where(s => s.LadderPosition > firstBreach.LadderPosition
                        && s.LossPercent <= lossThresholdPercent)];

        if (cleanAbove.Count > 0)
        {
            warnings.Add(
                $"Rung(s) above the first breach came back clean "
                + $"({string.Join(", ", cleanAbove.Select(s => s.RungLabel))}). "
                + "The ladder is not monotonic, so no single rate is the ceiling.");

            return new ThroughputVerdict(
                ThroughputLadderOutcome.Inconsistent, ceiling, firstBreach, warnings);
        }

        if (ceiling is null)
        {
            warnings.Add(
                $"The lowest rung offered ({firstBreach.RungLabel}) already exceeds "
                + $"{lossThresholdPercent:F1}%. The ladder starts above the ceiling.");
        }

        return new ThroughputVerdict(
            ThroughputLadderOutcome.Breached, ceiling, firstBreach, warnings);
    }

    // Describes the requested value.
    public static IReadOnlyList<string> Describe(ThroughputVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        var lines = new List<string>();

        foreach (string warning in verdict.Warnings)
        {
            lines.Add("  WARNING  " + warning);
        }

        switch (verdict.Outcome)
        {
            case ThroughputLadderOutcome.NoData:
                lines.Add("  VERDICT  no usable data.");
                break;

            case ThroughputLadderOutcome.NeverBreached:
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"  VERDICT  clean at every rung; at least {verdict.CleanCeiling!.ReceivedPerSecond:F0} frames/s. Push higher."));
                break;

            case ThroughputLadderOutcome.Inconsistent:
                lines.Add("  VERDICT  ladder is not monotonic; no single ceiling. Re-run the disputed rungs.");
                break;

            case ThroughputLadderOutcome.Breached:
                lines.Add(verdict.CleanCeiling is null
                    ? string.Create(CultureInfo.InvariantCulture,
                        $"  VERDICT  no clean rung; loss already {verdict.FirstBreach!.LossPercent:F2}% at {verdict.FirstBreach.RungLabel}.")
                    : string.Create(CultureInfo.InvariantCulture,
                        $"  VERDICT  clean ceiling {verdict.CleanCeiling.ReceivedPerSecond:F0} frames/s "
                        + $"({verdict.CleanCeiling.PayloadKbPerSecond:F0} KB/s), "
                        + $"first breach at offered {verdict.FirstBreach!.RungLabel} "
                        + $"with {verdict.FirstBreach.LossPercent:F2}% loss."));
                break;
        }

        return lines;
    }
}
