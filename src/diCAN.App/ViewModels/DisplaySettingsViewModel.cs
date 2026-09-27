using CommunityToolkit.Mvvm.ComponentModel;
using DiCAN.Core.Settings;

namespace DiCAN.App.ViewModels;

// Manages display settings.
public sealed partial class DisplaySettingsViewModel : ObservableObject
{

    public event EventHandler? Changed;

    [ObservableProperty]
    public partial bool ShowDeltaColumn { get; set; }

    [ObservableProperty]
    public partial bool ShowMinMaxDeltaColumns { get; set; }

    [ObservableProperty]
    public partial bool ShowOwnFramesInStream { get; set; } =
        DisplaySettingKeys.ShowOwnFramesInStream.Default;

    [ObservableProperty]
    public partial bool DecimalIds { get; set; }

    [ObservableProperty]
    public partial bool SpacedData { get; set; } = DisplaySettingKeys.SpacedData.Default;

    [ObservableProperty]
    public partial bool HighlightChanges { get; set; } =
        DisplaySettingKeys.HighlightChanges.Default;

    [ObservableProperty]
    public partial bool StreamDeltaTime { get; set; }

    [ObservableProperty]
    public partial double FontSize { get; set; } = DisplaySettingKeys.FontSize.Default;

    public RowFormat Format => new(
        DecimalIds: DecimalIds,
        SpacedData: SpacedData,
        ShowDelta: ShowDeltaColumn,
        ShowMinMaxDelta: ShowMinMaxDeltaColumns,
        HighlightChanges: HighlightChanges,
        StreamDeltaTime: StreamDeltaTime);

    public double RowHeight => Math.Round(FontSize * 1.85);

    // Handles property changes.
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(FontSize))
        {
            OnPropertyChanged(nameof(RowHeight));
        }

        if (e.PropertyName is not (nameof(Format) or nameof(RowHeight)))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        if (!_applying &&
            e.PropertyName is { } name &&
            Bindings.FirstOrDefault(b => b.PropertyName == name) is { } binding)
        {
            SwitchChanged?.Invoke(this, (binding.Setting, binding.Get(this)));
        }

        if (!_applying && e.PropertyName is nameof(FontSize))
        {
            FontSizeChanged?.Invoke(this, FontSize);
        }
    }

    public event EventHandler<double>? FontSizeChanged;

    public event EventHandler<(BoolSetting Setting, bool Value)>? SwitchChanged;

    private bool _applying;

    // Applies the requested changes.
    public void Apply(DisplaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _applying = true;

        try
        {
            foreach (SwitchBinding binding in Bindings)
            {
                binding.Set(this, binding.FromStored(settings));
            }

            FontSize = DisplaySettingKeys.FontSize.Clamp(settings.FontSize);
        }
        finally
        {
            _applying = false;
        }
    }

    // Stores switch binding data.
    private sealed record SwitchBinding(
        string PropertyName,
        BoolSetting Setting,
        Func<DisplaySettings, bool> FromStored,
        Func<DisplaySettingsViewModel, bool> Get,
        Action<DisplaySettingsViewModel, bool> Set);

    private static readonly SwitchBinding[] Bindings =
    [
        new(nameof(ShowDeltaColumn), DisplaySettingKeys.ShowDeltaColumn,
            s => s.ShowDeltaColumn, vm => vm.ShowDeltaColumn, (vm, v) => vm.ShowDeltaColumn = v),

        new(nameof(ShowMinMaxDeltaColumns), DisplaySettingKeys.ShowMinMaxDeltaColumns,
            s => s.ShowMinMaxDeltaColumns, vm => vm.ShowMinMaxDeltaColumns,
            (vm, v) => vm.ShowMinMaxDeltaColumns = v),

        new(nameof(ShowOwnFramesInStream), DisplaySettingKeys.ShowOwnFramesInStream,
            s => s.ShowOwnFramesInStream, vm => vm.ShowOwnFramesInStream,
            (vm, v) => vm.ShowOwnFramesInStream = v),

        new(nameof(DecimalIds), DisplaySettingKeys.DecimalIds,
            s => s.DecimalIds, vm => vm.DecimalIds, (vm, v) => vm.DecimalIds = v),

        new(nameof(SpacedData), DisplaySettingKeys.SpacedData,
            s => s.SpacedData, vm => vm.SpacedData, (vm, v) => vm.SpacedData = v),

        new(nameof(HighlightChanges), DisplaySettingKeys.HighlightChanges,
            s => s.HighlightChanges, vm => vm.HighlightChanges,
            (vm, v) => vm.HighlightChanges = v),

        new(nameof(StreamDeltaTime), DisplaySettingKeys.StreamDeltaTime,
            s => s.StreamDeltaTime, vm => vm.StreamDeltaTime, (vm, v) => vm.StreamDeltaTime = v),
    ];

    internal static IReadOnlyList<string> PersistedPropertyNames { get; } =
        [.. Bindings.Select(b => b.PropertyName)];
}
