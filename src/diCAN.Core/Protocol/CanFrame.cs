namespace DiCAN.Core.Protocol;

// Stores can frame data.
public sealed record CanFrame
{

    public required int Id { get; init; }

    public bool IsExtended { get; init; }

    public bool IsRemote { get; init; }

    public bool IsFd { get; init; }

    public bool IsBitRateSwitched { get; init; }

    public bool IsErrorStateIndicated { get; init; }

    public required ReadOnlyMemory<byte> Data { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public int RemoteLength { get; init; }

    public int MaxId => IsExtended ? 0x1FFFFFFF : 0x7FF;

    public bool IsValid =>
        Id >= 0 &&
        Id <= MaxId &&
        !(IsRemote && IsFd) &&
        !(IsBitRateSwitched && !IsFd) &&
        Data.Length <= (IsFd ? SlcanDlc.MaxPayload : SlcanDlc.MaxClassicPayload) &&
        !(IsRemote && Data.Length > 0) &&
        RemoteLength >= 0 &&
        RemoteLength <= SlcanDlc.MaxClassicPayload &&
        !(RemoteLength > 0 && !IsRemote);
}
