using Avalonia.Controls;
using Avalonia.Interactivity;
using DiCAN.App.ViewModels.Dialogs;

namespace DiCAN.App.Views.Dialogs;

// Manages export.
public partial class ExportDialog : Window
{

    // Initializes this instance.
    public ExportDialog() => InitializeComponent();

    public Func<ExportFormat, Task>? Export { get; set; }

    // Handles export click.
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ExportViewModel vm)
        {
            Close();
            return;
        }

        vm.Accept();

        if (Export is not { } write)
        {

            vm.ShowOutcome(vm.DoneMessage, omitted: 0, refused: false);
            Close();
            return;
        }

        await write(vm.Format);

        if (vm.IsFinished && !vm.NeedsAcknowledgement)
        {
            Close();
        }
    }

    // Handles cancel click.
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ExportViewModel vm)
        {
            vm.Cancel();
        }

        Close();
    }
}
