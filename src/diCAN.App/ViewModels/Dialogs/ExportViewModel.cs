using CommunityToolkit.Mvvm.ComponentModel;
using DiCAN.App.Localization;

namespace DiCAN.App.ViewModels.Dialogs;

// Defines export format values.
public enum ExportFormat
{

    Csv,

    Trc,
}

// Manages export.
public sealed partial class ExportViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    // Initializes this instance.
    public ExportViewModel(string recordingName, ILocalizationService localization)
    {
        _localization = localization;
        RecordingName = recordingName;
    }

    public string RecordingName { get; }

    [ObservableProperty]
    public partial bool IsWired { get; set; }

    public bool ShowNotWired => !IsWired && !IsFinished;

    [ObservableProperty]
    public partial ExportFormat Format { get; set; } = ExportFormat.Csv;

    public bool IsCsv
    {
        get => Format == ExportFormat.Csv;
        set
        {
            if (value)
            {
                Format = ExportFormat.Csv;
            }
        }
    }

    public bool IsTrc
    {
        get => Format == ExportFormat.Trc;
        set
        {
            if (value)
            {
                Format = ExportFormat.Trc;
            }
        }
    }

    public string DoneMessage => _localization["Export.Done"];

    public string RefusedMessage => _localization["Export.Refused"];

    public string FailedMessage => _localization["Export.Failed"];

    public ExportFormat? Result { get; private set; }

    [ObservableProperty]
    public partial bool IsFinished { get; set; }

    [ObservableProperty]
    public partial string Outcome { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRefused { get; set; }

    public bool NeedsAcknowledgement => IsRefused || _omittedInOutcome > 0;

    private long _omittedInOutcome;

    public bool OutcomeIsClean => IsFinished && !IsRefused && _omittedInOutcome == 0;

    public bool OutcomeIsPartial => IsFinished && !IsRefused && _omittedInOutcome > 0;

    public bool OutcomeIsRefused => IsFinished && IsRefused;

    // Shows the requested dialog.
    public void ShowOutcome(string message, long omitted, bool refused)
    {
        Outcome = message;
        _omittedInOutcome = omitted;
        IsRefused = refused;
        IsFinished = true;
        OnPropertyChanged(nameof(NeedsAcknowledgement));
        OnPropertyChanged(nameof(OutcomeIsClean));
        OnPropertyChanged(nameof(OutcomeIsPartial));
        OnPropertyChanged(nameof(OutcomeIsRefused));
        OnPropertyChanged(nameof(ShowNotWired));
    }

    // Accepts the current choice.
    public void Accept() => Result = Format;

    // Cancels the active operation.
    public void Cancel() => Result = null;

    // Handles is wired changed.
    partial void OnIsWiredChanged(bool value) => OnPropertyChanged(nameof(ShowNotWired));

    // Handles format changed.
    partial void OnFormatChanged(ExportFormat value)
    {
        OnPropertyChanged(nameof(IsCsv));
        OnPropertyChanged(nameof(IsTrc));

    }
}
