using System.Globalization;
using System.Text;
using DiCAN.Core.Protocol;
using DiCAN.Core.Streaming;

namespace DiCAN.Core.Recordings;

// Exports recorded CAN frames.
public static class CsvRecordingExporter
{

    public const string Columns = "time_s,dir,id,ext,fd,brs,esi,rtr,len,data,raw";

    // Writes output data.
    public static RecordingExportResult Write(
        TextWriter writer, RecordingExport export, IEnumerable<RecordedFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(frames);

        WriteHeader(writer, export);
        writer.WriteLine(Columns);

        long written = 0;
        var data = new StringBuilder(64);

        foreach (RecordedFrame frame in frames)
        {
            WriteRow(writer, frame, data);
            written++;
        }

        return new RecordingExportResult(written, 0, null);
    }

    // Writes header.
    private static void WriteHeader(TextWriter writer, RecordingExport export)
    {
        Recording recording = export.Recording;
        CultureInfo culture = CultureInfo.InvariantCulture;

        writer.WriteLine($"# diCAN {recording.AppVersion} recording export");

        writer.WriteLine($"# adapter: {recording.Adapter}");

        writer.WriteLine(
            $"# device: {recording.DeviceKey}" +
            $"{RecordingNaming.GenerationSuffix(recording.Port, recording.Generation)}" +
            $" on {recording.Port}");
        writer.WriteLine(
            $"# firmware: {recording.FirmwareBuild ?? "not reported"}");
        writer.WriteLine(
            $"# bitrate: {recording.Configuration.NominalBitrate.ToString(culture)} nominal" +
            (recording.Configuration.DataBitrate is { } dataRate
                ? $", {dataRate.ToString(culture)} data"
                : ", classic CAN"));
        writer.WriteLine($"# mode: {recording.Configuration.Mode}");
        writer.WriteLine(
            $"# started: {recording.StartedAt.UtcDateTime.ToString("o", culture)} " +
            $"(local offset {Offset(recording.StartedAt)})");
        writer.WriteLine(
            $"# ended: {(recording.EndedAt is { } ended ? ended.UtcDateTime.ToString("o", culture) : "not recorded")}");

        writer.WriteLine("# timestamps are HOST-side, not bus-level: they can repeat and can go backwards");

        WriteScopeAndFilters(writer, export);
        writer.WriteLine("#");
    }

    // Writes scope and filters.
    private static void WriteScopeAndFilters(TextWriter writer, RecordingExport export)
    {
        bool filtered = export.Recording.Scope == RecordingScope.FilteredOnly;

        writer.WriteLine(
            filtered
                ? "# scope: FILTERED -- only frames the filter passed were recorded"
                : "# scope: EVERYTHING -- no frame was filtered out");

        if (!filtered)
        {
            return;
        }

        if (export.FilterChanges.Count == 0)
        {
            writer.WriteLine("# filter: unchanged for the whole recording");
            return;
        }

        writer.WriteLine("# filter changes (seconds from start, then the expression):");

        foreach (RecordingFilterChange change in export.FilterChanges)
        {
            writer.WriteLine(
                $"#   {Seconds(change.AtMicroseconds)}  " +
                (change.Expression.Length == 0 ? "(no filter)" : change.Expression));
        }
    }

    // Writes row.
    private static void WriteRow(TextWriter writer, RecordedFrame recorded, StringBuilder data)
    {
        CanFrame frame = recorded.Frame;

        writer.Write(Seconds(recorded.TimeMicroseconds));
        writer.Write(',');
        writer.Write(recorded.Direction == CanFrameDirection.Transmitted ? "Tx" : "Rx");
        writer.Write(',');

        writer.Write(frame.Id.ToString(frame.IsExtended ? "X8" : "X3", CultureInfo.InvariantCulture));
        writer.Write(',');
        writer.Write(Bit(frame.IsExtended));
        writer.Write(',');
        writer.Write(Bit(frame.IsFd));
        writer.Write(',');
        writer.Write(Bit(frame.IsBitRateSwitched));
        writer.Write(',');
        writer.Write(Bit(frame.IsErrorStateIndicated));
        writer.Write(',');
        writer.Write(Bit(frame.IsRemote));
        writer.Write(',');

        writer.Write(
            (frame.IsRemote ? frame.RemoteLength : frame.Data.Length)
            .ToString(CultureInfo.InvariantCulture));
        writer.Write(',');

        data.Clear();

        if (!frame.IsRemote)
        {
            ReadOnlySpan<byte> bytes = frame.Data.Span;

            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                {
                    data.Append(' ');
                }

                data.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        writer.Write(data);
        writer.Write(',');

        writer.WriteLine(Escape(recorded.RawText));
    }

    // Formats a boolean bit.
    private static string Bit(bool value) => value ? "1" : "0";

    // Formats a time interval.
    internal static string Seconds(long microseconds)
    {
        long magnitude = Math.Abs(microseconds);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(microseconds < 0 ? "-" : string.Empty)}{magnitude / 1_000_000}.{magnitude % 1_000_000:D6}");
    }

    // Escapes the requested text.
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.AsSpan().IndexOfAny(",\"\n\r") >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    // Gets offset.
    private static string Offset(DateTimeOffset value) =>
        (value.Offset < TimeSpan.Zero ? "-" : "+") +
        value.Offset.Duration().ToString("hh\\:mm", CultureInfo.InvariantCulture);
}
