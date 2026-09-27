using Avalonia.Controls;
using DiCAN.App.ViewModels.Dialogs;

namespace DiCAN.App.Views.Dialogs;

// Manages options.
public partial class OptionsDialog : Window
{

    // Initializes this instance.
    public OptionsDialog()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (DataContext is OptionsDialogViewModel vm)
            {
                vm.CloseRequested += OnCloseRequested;
            }
        };

        Closed += (_, _) =>
        {
            if (DataContext is OptionsDialogViewModel vm)
            {
                vm.CloseRequested -= OnCloseRequested;
            }
        };
    }

    // Handles close requested.
    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
