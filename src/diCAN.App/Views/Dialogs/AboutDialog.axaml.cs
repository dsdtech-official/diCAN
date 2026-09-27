using Avalonia.Controls;
using Avalonia.Interactivity;
using DiCAN.App.Localization;

namespace DiCAN.App.Views.Dialogs;

// Manages about.
public partial class AboutDialog : Window
{
    private ILocalizationService? _localization;

    // Initializes this instance.
    public AboutDialog()
    {
        InitializeComponent();

        Opened += (_, _) => OkButton.Focus();
    }

    // Attaches the active session.
    public void Attach(ILocalizationService localization)
    {
        _localization = localization;
        BodyText.Text = localization.Format("About.Message", AppInfo.DisplayVersion);
    }

    // Handles ok click.
    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
