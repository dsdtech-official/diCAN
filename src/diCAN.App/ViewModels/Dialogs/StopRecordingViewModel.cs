namespace DiCAN.App.ViewModels.Dialogs;

// Manages recording stop.
public enum RecordingStopChoice
{

    Keep,

    Cancel,

    KeepAndExport,
}

// Manages stop recording.
public sealed class StopRecordingViewModel(bool isWired = false, bool closingPort = true)
{

    public bool IsWired { get; } = isWired;

    public bool ShowNotWired => !IsWired;

    public bool ClosingPort { get; } = closingPort;

    public bool StoppingOnly => !ClosingPort;

    public bool ExportWhenKept { get; set; } = true;
}
