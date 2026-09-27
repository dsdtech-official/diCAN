using Avalonia.Controls;
using Avalonia.Interactivity;
using DiCAN.App.Composition;
using DiCAN.App.Localization;
using DiCAN.App.ViewModels.Dialogs;
using DiCAN.Core.Recordings;
using DiCAN.Core.Settings;

namespace DiCAN.App.Views.Dialogs;

// Manages recordings.
public partial class RecordingsDialog : Window
{
    private IRecordingStore? _store;
    private ILocalizationService? _localization;
    private ISettingsStore? _settings;
    private bool _isRecording;

    // Initializes this instance.
    public RecordingsDialog() => InitializeComponent();

    // Attaches the active session.
    public void Attach(
        IRecordingStore? store,
        ILocalizationService localization,
        ISettingsStore? settings,
        bool isRecording)
    {
        _store = store;
        _localization = localization;
        _settings = settings;
        _isRecording = isRecording;

        ApplyRecordingState();
    }

    // Applies recording state.
    private void ApplyRecordingState()
    {
        if (DataContext is RecordingsListViewModel list)
        {
            list.IsRecordingInProgress = _isRecording;
        }
    }

    // Handles export click.
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (_store is not { } store ||
            _localization is not { } localization ||
            DataContext is not RecordingsListViewModel list ||
            list.Selected is not { } row)
        {
            return;
        }

        await RecordingExportRunner.RunAsync(this, store, row.Recording, localization, _settings);
    }

    // Handles delete click.
    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RecordingsListViewModel list && list.Selected is not null)
        {
            list.DeleteArmed = true;
        }
    }

    // Handles delete cancel click.
    private void OnDeleteCancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RecordingsListViewModel list)
        {
            list.DeleteArmed = false;
        }
    }

    // Handles a state change.
    private void OnDeleteFailedCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RecordingsListViewModel list)
        {
            list.DeleteFailed = false;
        }
    }

    // Handles delete confirm click.
    private async void OnDeleteConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (_store is not { } store ||
            _localization is not { } localization ||
            DataContext is not RecordingsListViewModel list)
        {
            return;
        }

        var logger = LoggingBootstrap.Current.Factory.CreateLogger("diCAN.Recordings");

        if (!await list.DeleteSelectedAsync(store, logger))
        {
            return;
        }

        DataContext = await RecordingsListViewModel.LoadAsync(store, localization, logger);

        ApplyRecordingState();
    }

    // Handles compact click.
    private async void OnCompactClick(object? sender, RoutedEventArgs e)
    {
        if (_store is not { } store || DataContext is not RecordingsListViewModel list)
        {
            return;
        }

        await list.CompactAsync(
            store, LoggingBootstrap.Current.Factory.CreateLogger("diCAN.Recordings"));
    }

    // Handles close click.
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
