using Avalonia.Controls;
using DiCAN.App.ViewModels.Dialogs;

namespace DiCAN.App.Views.Dialogs;

// Manages new session.
public partial class NewSessionDialog : Window
{

    // Initializes this instance.
    public NewSessionDialog() => InitializeComponent();

    // Handles data context changed.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is NewSessionDialogViewModel vm)
        {
            vm.CloseRequested += (_, _) => Close();

            vm.DetailedConfigurationRequested += async (_, _) =>
            {
                DetailedConfigurationViewModel panel = vm.CreateDetailedConfiguration();

                await new DetailedConfigurationDialog { DataContext = panel }.ShowDialog(this);

                vm.ApplyDetailedConfiguration(panel);
            };
        }
    }

    // Handles device double tapped.
    private void OnDeviceDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is NewSessionDialogViewModel { ConfirmCommand: { } confirm } &&
            confirm.CanExecute(null))
        {
            confirm.Execute(null);
        }
    }

    // Handles closed.
    protected override void OnClosed(EventArgs e)
    {

        (DataContext as NewSessionDialogViewModel)?.Detach();
        base.OnClosed(e);
    }
}
