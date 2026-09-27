using Avalonia;
using DiCAN.App.Composition;

namespace DiCAN.App;

// Manages program.
internal static class Program
{

    // Starts the application.
    [STAThread]
    public static void Main(string[] args)
    {

        LoggingBootstrap.Initialize(args);

        LoggingBootstrap.HookUnhandledExceptions();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Configures the application.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
