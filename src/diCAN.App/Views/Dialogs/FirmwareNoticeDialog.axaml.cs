using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DiCAN.App.Views.Dialogs;

// Manages firmware notice.
public partial class FirmwareNoticeDialog : Window
{

    // Initializes this instance.
    public FirmwareNoticeDialog()
    {
        InitializeComponent();

        Opened += (_, _) => ContinueButton.Focus();
    }

    // Handles continue click.
    private void OnContinueClick(object? sender, RoutedEventArgs e) => Close();
}
