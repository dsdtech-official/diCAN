using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DiCAN.App.Views.Dialogs;

// Manages clear history.
public partial class ClearHistoryDialog : Window
{

    // Initializes this instance.
    public ClearHistoryDialog()
    {
        InitializeComponent();

        Opened += (_, _) => CancelButton.Focus();
    }

    public bool Confirmed { get; private set; }

    // Handles confirm click.
    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Finish(confirmed: true);

    // Handles cancel click.
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Finish(confirmed: false);

    // Finishes the active operation.
    private void Finish(bool confirmed)
    {
        Confirmed = confirmed;
        Close();
    }
}
