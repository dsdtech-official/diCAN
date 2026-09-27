using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Platform;
using DiCAN.Core.Settings;
using DiCAN.App.Composition;
using DiCAN.App.Localization;
using DiCAN.App.ViewModels;
using DiCAN.App.ViewModels.Dialogs;
using DiCAN.App.Views.Dialogs;
using DiCAN.Core.Devices;
using DiCAN.Core.Recordings;
using DiCAN.Core.Transport;

namespace DiCAN.App.Views;

// Manages main window.
public partial class MainWindow : Window
{

    // Initializes this instance.
    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => AttachDisplaySettings();

        PositionChanged += OnMoved;
    }

    private DisplaySettingsViewModel? _display;

    // Attaches the active session.
    private void AttachDisplaySettings()
    {
        if (_display is not null)
        {
            _display.PropertyChanged -= OnDisplayChanged;
        }

        _display = (DataContext as MainWindowViewModel)?.Display;

        if (_display is null)
        {
            return;
        }

        _display.PropertyChanged += OnDisplayChanged;
        ApplyColumnVisibility();

        if (DataContext is MainWindowViewModel vm)
        {
            vm.StreamRows.CollectionChanged += OnStreamRowsChanged;
            vm.PropertyChanged += OnViewModelChanged;
            ApplySidebar(vm);
        }
    }

    private GridLength? _sidebarWidth;

    // Handles view model changed.
    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsSidebarOpen)
            && DataContext is MainWindowViewModel vm)
        {
            ApplySidebar(vm);
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsBottomPanelOpen))
        {
            ApplyBottomPanelFold();
        }
    }

    // Applies bottom panel height.
    private void ApplyBottomPanelHeight(double height)
    {
        _bottomPanelHeight = Math.Max(WindowPlacement.MinimumBottomPanelHeight, height);

        ApplyBottomPanelFold();
    }

    // Applies bottom panel fold.
    private void ApplyBottomPanelFold()
    {
        bool open = (DataContext as MainWindowViewModel)?.IsBottomPanelOpen ?? true;

        if (open is false && BottomSplit.RowDefinitions[2].Height is { IsAbsolute: true, Value: > 0 } current)
        {
            _bottomPanelHeight = current.Value;
        }

        BottomSplit.RowDefinitions[2].Height = open
            ? new GridLength(_bottomPanelHeight, GridUnitType.Pixel)
            : GridLength.Auto;
    }

    private double _bottomPanelHeight = WindowPlacement.DefaultBottomPanelHeight;

    // Gets current bottom.
    private double CurrentBottomPanelHeight()
    {

        if (BottomSplit.RowDefinitions[2].Height is { IsAbsolute: true, Value: > 0 } current)
        {
            _bottomPanelHeight = current.Value;
        }

        return double.IsFinite(_bottomPanelHeight) && _bottomPanelHeight > 0
            ? _bottomPanelHeight
            : WindowPlacement.DefaultBottomPanelHeight;
    }

    // Applies sidebar.
    private void ApplySidebar(MainWindowViewModel vm)
    {
        ColumnDefinition column = SplitGrid.ColumnDefinitions[1];

        if (vm.IsSidebarOpen)
        {
            column.MinWidth = 170;

            if (_sidebarWidth is { } remembered)
            {
                column.Width = remembered;
            }

            return;
        }

        if (column.Width.Value > 0)
        {
            _sidebarWidth = column.Width;
        }

        column.MinWidth = 0;
        column.Width = new GridLength(0);
    }

    private bool _followQueued;

    // Handles stream rows changed.
    private void OnStreamRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_followQueued || e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        _followQueued = true;

        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                _followQueued = false;

                if (DataContext is MainWindowViewModel vm && vm.StreamRows.Count > 0)
                {
                    StreamGrid.ScrollIntoView(vm.StreamRows[^1], null);
                }
            },
            Avalonia.Threading.DispatcherPriority.Background);
    }

    // Handles display changed.
    private void OnDisplayChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ApplyColumnVisibility();

    // Applies column visibility.
    private void ApplyColumnVisibility()
    {
        if (_display is null)
        {
            return;
        }

        foreach (DataGridColumn column in AggregateGrid.Columns)
        {
            column.IsVisible = column.Tag switch
            {
                "delta" => _display.ShowDeltaColumn,
                "minmax" => _display.ShowMinMaxDeltaColumns,
                _ => true,
            };
        }
    }

    // Handles send history picked.
    private void OnSendHistoryPicked(object? sender, SelectionChangedEventArgs e) =>
        TakeHistoryPick(sender, entry =>
            (DataContext as MainWindowViewModel)?.UseSendHistoryCommand.Execute(entry));

    // Handles filter box lost focus.
    private void OnFilterBoxLostFocus(object? sender, RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.CommitFilterCommand.Execute(null);

    // Handles filter history picked.
    private void OnFilterHistoryPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: HistoryItemViewModel row } box)
        {
            return;
        }

        box.SelectedItem = null;

        if (row.IsClearAction)
        {
            row.DeleteCommand.Execute(null);
            return;
        }

        (DataContext as MainWindowViewModel)?.UseFilterHistoryCommand.Execute(row.Text);
    }

    // Gets the next available item.
    private static void TakeHistoryPick(object? sender, Action<string> use)
    {
        if (sender is not ComboBox { SelectedItem: string entry } box)
        {
            return;
        }

        box.SelectedItem = null;
        use(entry);
    }

    // Gets the wire representation.
    public void Wire(
        ICanDeviceEnumerator enumerator,
        ICanSessionOpener opener,
        ICanDeviceWatcher watcher,
        ILocalizationService localization,
        IDeviceRegistry? registry = null,
        IRecordingStore? recordings = null,
        ISettingsStore? settings = null)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.RequestNewSession = async sessionType =>
        {
            var dialogViewModel = new NewSessionDialogViewModel(
                enumerator, opener, sessionType, localization, watcher, registry, settings: settings);
            var dialog = new NewSessionDialog { DataContext = dialogViewModel };

            dialogViewModel.ShowFirmwareNoticeAsync = async notice =>
                await new FirmwareNoticeDialog { DataContext = notice }.ShowDialog(dialog);

            await dialog.ShowDialog(this);

            return dialogViewModel.Result;
        };

        vm.RequestReconnect = async previous =>
        {
            CanDeviceInfo? device;

            try
            {

                device = enumerator.Enumerate()
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.DeviceId, previous.Device.DeviceId, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception scan)
            {

                vm.TransportMessage = localization["Reconnect.Failed"];
                vm.Journal.Error($"Reconnecting could not look for the adapter: {scan.Message}");
                return null;
            }

            if (device is null)
            {

                vm.TransportMessage = localization.Format("Session.ReconnectDeviceGone", previous.Title);
                return null;
            }

            try
            {

                return await opener.OpenAsync(
                    device,
                    previous.Generation,
                    previous.Configuration,
                    previous.ReconnectLabel);
            }
            catch (Exception open)
            {

                vm.TransportMessage = localization.Describe(open);
                return null;
            }
        };

        vm.RequestRecordingStopDecision = async closingPort =>
        {

            var dialog = new StopRecordingDialog
            {
                DataContext = new StopRecordingViewModel(
                    isWired: recordings is not null, closingPort: closingPort),
            };

            await dialog.ShowDialog(this);

            return dialog.Choice;
        };

        vm.RequestClearHistoryConfirmation = async stored =>
        {
            var dialog = new ClearHistoryDialog
            {
                DataContext = new ClearHistoryViewModel(localization, stored),
            };

            await dialog.ShowDialog(this);

            return dialog.Confirmed;
        };

        bool closeConfirmed = false;

        Closing += async (_, e) =>
        {
            if (closeConfirmed)
            {
                return;
            }

            e.Cancel = true;

            if (await vm.ConfirmClosingAsync())
            {
                closeConfirmed = true;
                Close();
            }
        };

        vm.RequestAbout = async () =>
        {
            var about = new AboutDialog();
            about.Attach(localization);
            await about.ShowDialog(this);
        };

        vm.RequestOptions = async () =>
        {
            if (settings is not { } store)
            {
                return;
            }

            AppOptions current = await store.LoadOptionsAsync();
            var options = new OptionsDialogViewModel(localization, current);
            var dialog = new OptionsDialog { DataContext = options };

            await dialog.ShowDialog(this);

            if (options.Result is { } chosen)
            {
                await store.SaveOptionsAsync(chosen);
            }
        };

        vm.RequestRecordings = async () =>
        {

            RecordingsListViewModel list = recordings is { } store
                ? await RecordingsListViewModel.LoadAsync(
                    store, localization, LoggingBootstrap.Current.Factory.CreateLogger("diCAN.Recordings"))
                : new RecordingsListViewModel(localization);

            var dialog = new RecordingsDialog { DataContext = list };

            dialog.Attach(
                store: recordings, localization: localization, settings: settings,
                isRecording: vm.IsRecording);

            await dialog.ShowDialog(this);
        };

        vm.RequestExport = async () =>
        {
            if (recordings is not { } store)
            {
                await new ExportDialog
                {
                    DataContext = new ExportViewModel(vm.RecordingAdapterLabel, localization),
                }.ShowDialog(this);

                return;
            }

            RecordingLibrary library = await store.ListLibraryAsync();
            IReadOnlyList<Recording> all = library.Recordings;

            bool newestMayBeUnreadable = library.UnreadableVolumes.Any(volume =>
                all.Count == 0 || string.CompareOrdinal(volume.VolumeKey, all[0].Volume) >= 0);

            if (all.Count == 0 || newestMayBeUnreadable)
            {

                var manager = new RecordingsDialog
                {
                    DataContext = await RecordingsListViewModel.LoadAsync(
                        store, localization, LoggingBootstrap.Current.Factory.CreateLogger("diCAN.Recordings")),
                };
                manager.Attach(
                    store: recordings, localization: localization, settings: settings,
                    isRecording: vm.IsRecording);

                await manager.ShowDialog(this);

                return;
            }

            await RecordingExportRunner.RunAsync(this, store, all[0], localization, settings);
        };
    }

    // Handles exit click.
    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    // Handles copy log click.
    private async void OnCopyLogClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        await PutOnClipboardAsync(vm.Log.CopyText());
    }

    // Handles copy log row click.
    private async void OnCopyLogRowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await PutOnClipboardAsync(vm.Log.CopySelectedText());
        }
    }

    // Handles log context requested.
    private void OnLogContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        ListBoxItem? item = source as ListBoxItem ?? source.FindAncestorOfType<ListBoxItem>();

        if (item?.DataContext is SessionLogRow row && DataContext is MainWindowViewModel vm)
        {
            vm.Log.SelectedRow = row;
        }
        else if (DataContext is MainWindowViewModel outside)
        {

            outside.Log.SelectedRow = null;
        }
    }

    // Handles a state change.
    private void OnAggregateContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        DataGridRow? row = e.Source is Visual source
            ? source as DataGridRow ?? source.FindAncestorOfType<DataGridRow>()
            : null;

        vm.SelectedRow = row?.DataContext as CanIdRowViewModel;
    }

    // Handles a state change.
    private void OnTransmitContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || e.Source is not Visual source)
        {
            return;
        }

        if ((source as DataGridRow ?? source.FindAncestorOfType<DataGridRow>())
            is { DataContext: TransmitRowViewModel row })
        {
            vm.SelectedTransmitRow = row;
        }
    }

    // Handles a state change.
    private async void OnCopyDetailPayloadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await PutOnClipboardAsync(vm.CopyDetailPayloadText() ?? string.Empty);
        }
    }

    // Handles a state change.
    private async void OnCopyAggregateIdClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await PutOnClipboardAsync(vm.CopySelectedIdText() ?? string.Empty);
        }
    }

    // Handles a state change.
    private async void OnCopyAggregateDataClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await PutOnClipboardAsync(vm.CopySelectedDataText() ?? string.Empty);
        }
    }

    // Handles save log click.
    private async void OnSaveLogClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "dican-session-log.txt",
            DefaultExtension = "txt",
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using Stream stream = await file.OpenWriteAsync();
            await using StreamWriter writer = new(stream);

            await writer.WriteAsync(vm.Log.SaveHeader(LoggingBootstrap.Current.LogDirectory));
            await writer.WriteAsync(vm.Log.CopyText());
        }
        catch (Exception error)
        {

            vm.Journal.Error($"Could not save the session log: {error.Message}");
        }
    }

    // Copies text to the clipboard.
    private async Task PutOnClipboardAsync(string text)
    {
        if (Clipboard is not { } clipboard || text.Length == 0)
        {
            return;
        }

        try
        {
            await clipboard.SetValueAsync(Avalonia.Input.DataFormat.Text, text);
        }
        catch (Exception)
        {

        }
    }

    // Handles open log folder click.
    private void OnOpenLogFolderClick(object? sender, RoutedEventArgs e)
    {

        string folder = LoggingBootstrap.Current.LogDirectory
            ?? LoggingBootstrap.LogDirectoryFor(LoggingBootstrap.Current.Options.DataDirectory)
            ?? string.Empty;

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception)
        {

        }
    }

    // Applies placement.
    public void ApplyPlacement(WindowPlacement stored)
    {

        Screen? screen = Screens?.Primary;

        if (stored.HasPosition && Screens is { } screens)
        {
            screen = screens.ScreenFromPoint(new PixelPoint((int)stored.X, (int)stored.Y)) ?? screen;
        }

        if (screen is null)
        {
            return;
        }

        double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        PixelRect work = screen.WorkingArea;

        WindowPlacement fitted = stored.ClampTo(
            work.X / scale, work.Y / scale, work.Width / scale, work.Height / scale);

        Width = fitted.Width;
        Height = fitted.Height;

        ApplyBottomPanelHeight(fitted.BottomPanelHeight);

        if (!double.IsNaN(fitted.SidebarWidth) && fitted.SidebarWidth > 0)
        {
            _sidebarWidth = new GridLength(fitted.SidebarWidth);
        }

        if (DataContext is MainWindowViewModel restoring)
        {
            restoring.IsSidebarOpen = fitted.SidebarOpen;
        }

        if (fitted.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        if (!fitted.HasPosition)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;

        var target = new PixelPoint((int)(fitted.X * scale), (int)(fitted.Y * scale));

        // Positions the application UI.
        void PlaceOnce(object? sender, EventArgs e)
        {
            Opened -= PlaceOnce;

            if (WindowState != WindowState.Maximized)
            {
                Position = target;
            }
        }

        Opened += PlaceOnce;
    }

    // Gets current placement.
    public WindowPlacement CurrentPlacement()
    {
        bool maximized = WindowState == WindowState.Maximized;

        double panel = CurrentBottomPanelHeight();

        double sidebar = CurrentSidebarWidth();
        bool sidebarOpen = (DataContext as MainWindowViewModel)?.IsSidebarOpen ?? true;

        if (maximized)
        {
            return WindowPlacement.Default with
            {
                X = _lastX,
                Y = _lastY,
                Maximized = true,
                BottomPanelHeight = panel,
                SidebarWidth = sidebar,
                SidebarOpen = sidebarOpen,
            };
        }

        return new WindowPlacement(
            _lastX, _lastY, Width, Height, Maximized: false, panel, sidebar, sidebarOpen);
    }

    // Gets current sidebar width.
    private double CurrentSidebarWidth()
    {
        if (_sidebarWidth is { } collapsed && collapsed.Value > 0)
        {
            return collapsed.Value;
        }

        double live = SplitGrid.ColumnDefinitions[1].Width.Value;

        return live > 0 ? live : WindowPlacement.NotRemembered;
    }

    private double _lastX = double.NaN;
    private double _lastY = double.NaN;

    // Handles moved.
    private void OnMoved(object? sender, PixelPointEventArgs e)
    {
        if (WindowState != WindowState.Maximized)
        {
            _lastX = e.Point.X / RenderScaling;
            _lastY = e.Point.Y / RenderScaling;
        }
    }
}
