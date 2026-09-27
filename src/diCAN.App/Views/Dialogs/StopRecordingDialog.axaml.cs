using Avalonia.Controls;
using Avalonia.Interactivity;
using DiCAN.App.ViewModels.Dialogs;

namespace DiCAN.App.Views.Dialogs;

// Manages stop recording.
public partial class StopRecordingDialog : Window
{

    // Initializes this instance.
    public StopRecordingDialog()
    {
        InitializeComponent();

        Opened += (_, _) => KeepButton.Focus();
    }

    public RecordingStopChoice Choice { get; private set; } = RecordingStopChoice.Cancel;

    // Handles keep click.
    private void OnKeepClick(object? sender, RoutedEventArgs e) =>
        Finish(DataContext is StopRecordingViewModel { ExportWhenKept: true }
            ? RecordingStopChoice.KeepAndExport
            : RecordingStopChoice.Keep);

    // Handles cancel click.
    private void OnCancelClick(object? sender, RoutedEventArgs e) =>
        Finish(RecordingStopChoice.Cancel);

    // Finishes the active operation.
    private void Finish(RecordingStopChoice choice)
    {
        Choice = choice;
        Close();
    }
}
