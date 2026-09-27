using DiCAN.Core.Protocol;
using DiCAN.Core.Streaming;

namespace DiCAN.App.ViewModels;

// Manages frame stream row.
public sealed class FrameStreamRowViewModel
{

    // Initializes this instance.
    private FrameStreamRowViewModel(
        long sequence, string time, string id, string direction, bool isTransmitted,
        string length, string type, string data, string dataHex, bool isExtended, bool isGap)
    {
        Sequence = sequence;
        Time = time;
        Id = id;
        Direction = direction;
        IsTransmitted = isTransmitted;
        Length = length;
        Type = type;
        Data = data;
        FullDataHex = dataHex;
        IsExtended = isExtended;
        IsGap = isGap;
    }

    public string FullDataHex { get; }

    public bool IsExtended { get; }

    public long Sequence { get; }

    public string Time { get; }

    public string Direction { get; }

    public bool IsTransmitted { get; }

    public string Id { get; }

    public string Length { get; }

    public string Type { get; }

    public string Data { get; }

    public bool IsGap { get; }

    public string FullData => Data;

    // Converts the requested value.
    public static FrameStreamRowViewModel FromEntry(
        CanFrameLogEntry entry, DateTimeOffset origin, RowFormat format, DateTimeOffset? previous)
    {
        CanFrame frame = entry.Frame;

        string time = format.StreamDeltaTime
            ? FormatDelta(previous is { } p ? frame.Timestamp - p : null)
            : FormatTime(frame.Timestamp - origin);

        bool transmitted = entry.Direction == CanFrameDirection.Transmitted;

        string hex = Convert.ToHexString(frame.Data.Span);

        return new FrameStreamRowViewModel(
            entry.Sequence,
            time,
            FormatId(frame.Id, frame.IsExtended, format.DecimalIds),
            transmitted ? "Tx" : "Rx",
            transmitted,
            FormatLength(frame),
            FormatType(frame),
            CanIdRowViewModel.Spaced(hex, format.SpacedData),
            hex,
            frame.IsExtended,
            isGap: false);
    }

    // Formats length.
    private static string FormatLength(CanFrame frame) =>
        (frame.IsRemote ? frame.RemoteLength : frame.Data.Length)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Formats type.
    private static string FormatType(CanFrame frame) => frame switch
    {
        { IsRemote: true } => "RTR",
        { IsFd: true, IsBitRateSwitched: true } => "FD BRS",
        { IsFd: true } => "FD",
        _ => string.Empty,
    };

    // Formats delta.
    private static string FormatDelta(TimeSpan? since) => since switch
    {
        null => "-",
        { TotalMilliseconds: >= 1000 } d => $"{d.TotalSeconds:0.00} s",
        { } d => $"{Math.Max(0, d.TotalMilliseconds):0.000}",
    };

    // Gets gap.
    public static FrameStreamRowViewModel Gap(long sequence, string message) =>
        new(sequence, "——", string.Empty, string.Empty, isTransmitted: false,
            string.Empty, string.Empty, message, string.Empty, isExtended: false, isGap: true);

    // Formats time.
    private static string FormatTime(TimeSpan since)
    {

        if (since < TimeSpan.Zero)
        {
            since = TimeSpan.Zero;
        }

        return $"{(int)since.TotalMinutes:00}:{since.Seconds:00}.{since.Milliseconds:000}";
    }

    // Formats id.
    private static string FormatId(int id, bool isExtended, bool decimalIds) => decimalIds
        ? id.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : isExtended ? $"{id:X8}" : $"{id:X3}";

}
