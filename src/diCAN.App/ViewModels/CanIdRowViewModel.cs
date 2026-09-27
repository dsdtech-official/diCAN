using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DiCAN.Core.Aggregation;
using DiCAN.Core.Protocol;

namespace DiCAN.App.ViewModels;

// Manages can id row.
public sealed partial class CanIdRowViewModel : ObservableObject
{

    private byte[] _payload = [];

    // Initializes this instance.
    public CanIdRowViewModel(CanIdRow row, RowFormat format)
    {

        Update(row, format);
    }

    public long Key { get; private set; }

    [ObservableProperty]
    public partial string Id { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Direction { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasTransmitted { get; set; }

    [ObservableProperty]
    public partial long Count { get; set; }

    [ObservableProperty]
    public partial string Data { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Length { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Type { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Period { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Delta { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MinDelta { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MaxDelta { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasChanged { get; set; }

    public string FullData { get; private set; } = string.Empty;

    public string FullDataHex { get; private set; } = string.Empty;

    public bool IsExtended { get; private set; }

    public string IdHex { get; private set; } = string.Empty;

    public bool IsFd { get; private set; }

    public bool IsBitRateSwitched { get; private set; }

    public bool IsRemote { get; private set; }

    public int RemoteLength { get; private set; }

    // Converts the requested value.
    public CanFrame ToFrame() => new()
    {

        Id = (int)(Key >> 1),
        IsExtended = IsExtended,
        Data = IsRemote ? ReadOnlyMemory<byte>.Empty : _payload.ToArray(),
        IsFd = IsFd,
        IsBitRateSwitched = IsBitRateSwitched,
        IsRemote = IsRemote,
        RemoteLength = RemoteLength,
    };

    // Updates the current state.
    public void Update(CanIdRow row, RowFormat format)
    {
        string hex = Convert.ToHexString(row.Data.Span);
        string data = Spaced(hex, format.SpacedData);

        HasChanged = format.HighlightChanges && FullData.Length > 0 && !string.Equals(
            FullData, data, StringComparison.Ordinal);

        Key = Identity(row);
        Id = FormatId(row, format.DecimalIds);
        Direction = FormatDirection(row);

        HasTransmitted = row.SawTransmitted;
        Count = row.Count;
        Data = data;
        FullData = data;
        FullDataHex = hex;
        IsExtended = row.IsExtended;

        IdHex = row.Id.ToString(row.IsExtended ? "X8" : "X3", CultureInfo.InvariantCulture);
        IsFd = row.IsFd;
        IsBitRateSwitched = row.IsBitRateSwitched;
        IsRemote = row.IsRemote;
        RemoteLength = row.RemoteLength;
        Length = FormatLength(row);
        Type = FormatType(row);
        Period = FormatPeriod(row.Period);

        bool observed = row.Count > 1;

        Delta = FormatInterval(row.LastDelta, observed);
        MinDelta = FormatInterval(row.MinDelta, observed);
        MaxDelta = FormatInterval(row.MaxDelta, observed);

        if (_payload.Length != row.Data.Length)
        {
            _payload = new byte[row.Data.Length];
        }

        row.Data.Span.CopyTo(_payload);
    }

    // Gets identity.
    public static long Identity(CanIdRow row) => ((long)row.Id << 1) | (row.IsExtended ? 1L : 0L);

    // Formats id.
    private static string FormatId(CanIdRow row, bool decimalIds) => decimalIds
        ? row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : row.IsExtended ? $"{row.Id:X8}" : $"{row.Id:X3}";

    // Formats length.
    private static string FormatLength(CanIdRow row) =>
        (row.IsRemote ? row.RemoteLength : row.Data.Length)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Formats type.
    private static string FormatType(CanIdRow row) => row switch
    {
        { IsRemote: true } => "RTR",
        { IsFd: true, IsBitRateSwitched: true } => "FD BRS",
        { IsFd: true } => "FD",
        _ => string.Empty,
    };

    // Formats direction.
    private static string FormatDirection(CanIdRow row) => (row.SawReceived, row.SawTransmitted) switch
    {
        (true, true) => "Rx+Tx",
        (false, true) => "Tx",
        _ => "Rx",
    };

    // Formats hexadecimal bytes.
    internal static string Spaced(string hex, bool spaced)
    {
        if (hex.Length == 0)
        {
            return string.Empty;
        }

        if (!spaced)
        {
            return hex;
        }

        return string.Create(hex.Length + ((hex.Length / 2) - 1), hex, static (span, source) =>
        {
            int write = 0;

            for (int i = 0; i < source.Length; i += 2)
            {
                if (i > 0)
                {
                    span[write++] = ' ';
                }

                span[write++] = source[i];
                span[write++] = source[i + 1];
            }
        });
    }

    // Formats period.
    private static string FormatPeriod(TimeSpan? period) => period switch
    {
        null => "-",
        { TotalMilliseconds: >= 1000 } p => $"{p.TotalSeconds:0.00} s",
        { } p => $"{p.TotalMilliseconds:0.0} ms",
    };

    // Formats interval.
    private static string FormatInterval(TimeSpan interval, bool observed) => observed
        ? $"{interval.TotalMilliseconds:0.0}"
        : "-";
}
