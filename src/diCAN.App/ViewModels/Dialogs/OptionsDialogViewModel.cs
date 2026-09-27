using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiCAN.App.Localization;
using DiCAN.Core.Settings;

namespace DiCAN.App.ViewModels.Dialogs;

// Manages options dialog.
public sealed partial class OptionsDialogViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly AppOptions _original;

    // Initializes this instance.
    public OptionsDialogViewModel(ILocalizationService localization, AppOptions current)
    {
        ArgumentNullException.ThrowIfNull(localization);

        _localization = localization;
        _original = current;

        DevicePollMs = IntSetting.Render(current.DevicePollMs);
        StreamCapacity = IntSetting.Render(current.StreamCapacity);
        AutoReconnect = current.AutoReconnect;
        ReconnectAttempts = IntSetting.Render(current.ReconnectAttempts);
        ReconnectDelayMs = IntSetting.Render(current.ReconnectDelayMs);
        RecordRawText = current.RecordRawText;
        RecordFilteredOnly = current.RecordFilteredOnly;
    }

    [ObservableProperty]
    public partial string DevicePollMs { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StreamCapacity { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool AutoReconnect { get; set; }

    [ObservableProperty]
    public partial string ReconnectAttempts { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReconnectDelayMs { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RecordRawText { get; set; }

    [ObservableProperty]
    public partial bool RecordFilteredOnly { get; set; }

    public AppOptions? Result { get; private set; }

    public event EventHandler? CloseRequested;

    // Accepts the current choice.
    [RelayCommand]
    private void Accept()
    {
        Result = new AppOptions(
            Read(DevicePollMs, _original.DevicePollMs),
            Read(StreamCapacity, _original.StreamCapacity),
            AutoReconnect,
            Read(ReconnectAttempts, _original.ReconnectAttempts),
            Read(ReconnectDelayMs, _original.ReconnectDelayMs),
            RecordRawText,
            RecordFilteredOnly).Clamped();

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Rejects the current choice.
    [RelayCommand]
    private void Reject()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Reads input data.
    private static int Read(string text, int fallback) =>
        int.TryParse(
            text,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int value)
            ? value
            : fallback;

    // Gets localized text.
    private string L(string key) => _localization[key];

    public string Title => L("Options.Title");
    public string TabConnection => L("Options.Tab.Connection");
    public string TabDisplay => L("Options.Tab.Display");
    public string TabRecording => L("Options.Tab.Recording");
    public string RecordFilteredOnlyLabel => L("Options.RecordFilteredOnly");
    public string RecordRawTextLabel => L("Options.RecordRawText");
    public string AutoReconnectLabel => L("Options.AutoReconnect");
    public string AttemptsLabel => L("Options.AttemptsLabel");
    public string DelayLabel => L("Options.DelayLabel");
    public string PollLabel => L("Options.PollLabel");
    public string CapacityLabel => L("Options.CapacityLabel");

    public string RestartNote => L("Options.RestartNote");
    public string OkText => L("Common.Ok");
    public string CancelText => L("Common.Cancel");
}
