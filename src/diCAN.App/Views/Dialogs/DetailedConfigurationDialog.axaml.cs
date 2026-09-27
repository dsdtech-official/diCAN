using Avalonia.Controls;
using Avalonia.Interactivity;
using DiCAN.App.ViewModels.Dialogs;

namespace DiCAN.App.Views.Dialogs;

// Manages adapter configuration.
public partial class DetailedConfigurationDialog : Window
{

    // Initializes this instance.
    public DetailedConfigurationDialog() => InitializeComponent();

    // Handles accept.
    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DetailedConfigurationViewModel vm)
        {
            vm.Accepted = true;
        }

        Close();
    }

    // Handles cancel.
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
