using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DiCAN.App.Composition;
using DiCAN.App.Localization;
using DiCAN.App.ViewModels;
using DiCAN.App.ViewModels.Dialogs;
using DiCAN.App.Views;
using DiCAN.Core.Abstractions;
using DiCAN.Core.Devices;
using DiCAN.Core.Diagnostics;
using DiCAN.Core.History;
using DiCAN.Core.Localization;
using DiCAN.Core.Recordings;
using DiCAN.Core.Sending;
using DiCAN.Core.Settings;
using DiCAN.Infrastructure.Devices;
using DiCAN.Infrastructure.History;
using DiCAN.Infrastructure.Recordings;
using DiCAN.Infrastructure.Sending;
using DiCAN.Infrastructure.Settings;
using DiCAN.Infrastructure.Time;
using DiCAN.Mac;
using DiCAN.Mac.Usb;
using DiCAN.Windows;
using DiCAN.Windows.Usb;
#if DEBUG
using DiCAN.App.Demo;
#endif
using DiCAN.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace DiCAN.App;

// Manages app.
public partial class App : Application
{

    // Initializes the component.
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    // Initializes the application.
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {

            CommandLineOptions options = LoggingBootstrap.Current.Options;

            (ICanDeviceEnumerator enumerator, ICanSessionOpener opener) = Compose(options);

            ILoggerFactory loggers = LoggingBootstrap.Current.Factory;

            var localization = new LocalizationService(loggers.CreateLogger<LocalizationService>());
            LocalizationService.InstallAsCurrent(localization);

            ISystemUiLanguage systemLanguage = OperatingSystem.IsMacOS()
                ? new MacSystemUiLanguage()
                : NullSystemUiLanguage.Instance;

            ISettingsStore? settings = ComposeSettings();

            SyncSettingsCatalog(settings);

            ApplyStartupLanguage(localization, systemLanguage, settings);

            if (settings is not null)
            {
                ILogger languageLog = loggers.CreateLogger("diCAN.Language");

                localization.CultureChanged += (_, _) =>
                {

                    _ = settings.SaveTextAsync(
                            TextSettingKeys.Language,
                            localization.CurrentCulture.Name)
                        .ContinueWith(
                            t => languageLog.LogWarning(
                                t.Exception, "Could not remember the interface language."),
                            TaskContinuationOptions.OnlyOnFaulted);
                };
            }

            AppOptions appOptions = LoadOptions(settings);

            var watcher = new PollingCanDeviceWatcher(
                enumerator,
                logger: loggers.CreateLogger<PollingCanDeviceWatcher>(),
                pollInterval: TimeSpan.FromMilliseconds(appOptions.DevicePollMs));

            ITimerResolution timerResolution = OperatingSystem.IsWindows()
                ? new TimerResolution()
                : NullTimerResolution.Instance;

            IDeviceRegistry? registry = ComposeRegistry();
            IRecordingStore? recordings = ComposeRecordings();
            IHistoryStore? history = ComposeHistory();
            ITransmitQueueStore? transmitQueue = ComposeTransmitQueue();

            var viewModel = new MainWindowViewModel(
                localization, timerResolution, registry, recordings, history, settings, appOptions,
                transmitQueue)
            {

                WindowTitle = options.Demo
                    ? "diCAN  ⚠ SIMULATED DATA — NOT A REAL BUS"
                    : "diCAN",
            };

            ILogger sessionLog = loggers.CreateLogger("diCAN.Session");

            Action flushRepeats;

            var repeats = new RepeatedLineLimiter();

            // Maps the logging level.
            LogLevel ToLevel(SessionEventLevel level) => level switch
            {
                SessionEventLevel.Error => LogLevel.Error,
                SessionEventLevel.Warning => LogLevel.Warning,
                _ => LogLevel.Information,
            };

            viewModel.Journal.Recorded += (_, entry) =>
            {
                LimiterDecision decision = repeats.Next(entry.Key, entry.Message);

                if (decision.Summary is { } summary)
                {
                    sessionLog.Log(ToLevel(entry.Level), "{Message}", summary);
                }

                if (decision.Line is { } line)
                {
                    sessionLog.Log(ToLevel(entry.Level), "{Message}", line);
                }
            };

            flushRepeats = () =>
            {
                if (repeats.Flush() is { } tail)
                {
                    sessionLog.LogWarning("{Message}", tail);
                }
            };

            var window = new MainWindow { DataContext = viewModel };
            window.Wire(enumerator, opener, watcher, localization, registry, recordings, settings);

            if (settings is not null)
            {
                window.ApplyPlacement(LoadPlacement(settings));
            }

            desktop.MainWindow = window;

            bool cleanupDone = false;

            desktop.ShutdownRequested += async (_, e) =>
            {
                if (cleanupDone)
                {
                    return;
                }

                e.Cancel = true;
                cleanupDone = true;

                await viewModel.ShutdownAsync();
                await watcher.DisposeAsync();

                if (settings is not null)
                {
                    try
                    {
                        await settings.SaveWindowAsync(window.CurrentPlacement());
                    }
                    catch (Exception error)
                    {
                        Swallowed("Saving the window placement", error);
                    }
                }

                flushRepeats();

                LoggingBootstrap.Shutdown();

                desktop.Shutdown();
            };

            _ = watcher.StartAsync();

            _ = viewModel.LoadHistoryAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Applies startup language.
    private static void ApplyStartupLanguage(
        LocalizationService localization, ISystemUiLanguage systemLanguage, ISettingsStore? settings)
    {

        string? chosen = settings is null
            ? null
            : SafeLoad(settings);

        if (!string.IsNullOrEmpty(chosen))
        {
            LanguageOption? remembered = localization.AvailableLanguages.FirstOrDefault(l =>
                string.Equals(l.CultureName, chosen, StringComparison.OrdinalIgnoreCase));

            if (remembered is not null)
            {
                localization.SetLanguage(remembered.Culture);
                return;
            }
        }

        ApplyOperatingSystemLanguage(localization, systemLanguage);

        // Loads saved data safely.
        static string? SafeLoad(ISettingsStore store)
        {
            try
            {
                return store.LoadTextAsync(TextSettingKeys.Language).GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                Swallowed("Reading the stored language", error);
                return null;
            }
        }
    }

    // Loads options.
    private static AppOptions LoadOptions(ISettingsStore? settings)
    {
        if (settings is null)
        {
            return AppOptions.Default;
        }

        try
        {
            return settings.LoadOptionsAsync().GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Swallowed("Reading the stored options", error);

            return AppOptions.Default;
        }
    }

    // Synchronizes the current view.
    private static void SyncSettingsCatalog(ISettingsStore? settings)
    {
        if (settings is null)
        {
            return;
        }

        try
        {
            settings.SyncAsync(SettingsCatalog.All).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Swallowed("Seeding the settings catalog", error);

        }
    }

    // Applies the requested changes.
    private static void ApplyOperatingSystemLanguage(
        LocalizationService localization, ISystemUiLanguage systemLanguage)
    {
        CultureInfo os = systemLanguage.GetPreferred() ?? CultureInfo.CurrentUICulture;

        string[] available = [.. localization.AvailableLanguages.Select(l => l.CultureName)];

        if (UiLanguageMatch.Best(os, available) is not { } name)
        {
            return;
        }

        LanguageOption? match = localization.AvailableLanguages.FirstOrDefault(l =>
            string.Equals(l.CultureName, name, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {

            localization.SetLanguage(match.Culture);
        }
    }

    // Logs a recoverable error.
    private static void Swallowed(string what, Exception error)
    {
        try
        {
            LoggingBootstrap.Current.Factory
                .CreateLogger("diCAN.Startup")
                .LogWarning(error, "{What} failed; continuing without it.", what);
        }
        catch (Exception)
        {
        }
    }

    // Resolves data locations.
    private static AppPaths Paths() =>
        new(LoggingBootstrap.Current.Options.DataDirectory, LoggingBootstrap.DataFolderName);

    // Creates application services.
    private static ISettingsStore? ComposeSettings()
    {
        try
        {
            return new SqliteSettingsStore(Paths().UserDataDatabasePath);
        }
        catch (Exception error)
        {
            Swallowed("Opening the settings store", error);
            return null;
        }
    }

    // Loads placement.
    private static WindowPlacement LoadPlacement(ISettingsStore settings)
    {
        try
        {
            return Task.Run(() => settings.LoadWindowAsync()).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {

            Swallowed("Reading the stored window placement", error);
            return WindowPlacement.Default;
        }
    }

    // Creates application services.
    private static IRecordingStore? ComposeRecordings()
    {
        try
        {

            return new SqliteRecordingStore(Paths(), new MonotonicClock());
        }
        catch (Exception error)
        {
            Swallowed("Opening the recordings folder", error);
            return null;
        }
    }

    // Creates application services.
    private static IHistoryStore? ComposeHistory()
    {
        try
        {
            return new SqliteHistoryStore(Paths().UserDataDatabasePath, new MonotonicClock());
        }
        catch (Exception error)
        {
            Swallowed("Opening the filter and send history", error);
            return null;
        }
    }

    // Creates application services.
    private static ITransmitQueueStore? ComposeTransmitQueue()
    {
        try
        {
            return new SqliteTransmitQueueStore(Paths().UserDataDatabasePath);
        }
        catch (Exception error)
        {
            Swallowed("Opening the transmit queue store", error);
            return null;
        }
    }

    // Creates application services.
    private static IDeviceRegistry? ComposeRegistry()
    {
        try
        {

            var paths = Paths();

            return new SqliteDeviceRegistry(
                paths.UserDataDatabasePath,
                new MonotonicClock(),
                typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown");
        }
        catch (Exception error)
        {

            Swallowed("Opening the adapter registry", error);
            return null;
        }
    }

    // Creates application services.
    private static (ICanDeviceEnumerator, ICanSessionOpener) Compose(CommandLineOptions options)
    {

        if (!options.Demo)
        {

            if (OperatingSystem.IsWindows())
            {

                var openers = new CanSessionOpener(
                    new SlcanSessionOpener(),
                    new GsUsbSessionOpener(WinUsbBulkDevice.OpenBySerialNumber));

                return (new WindowsCanDeviceEnumerator(), openers);
            }

            if (OperatingSystem.IsMacOS())
            {
                var openers = new CanSessionOpener(
                    new SlcanSessionOpener(),
                    new GsUsbSessionOpener(MacUsbBulkDevice.OpenBySerialNumber));

                return (new MacCanDeviceEnumerator(), openers);
            }

            throw new PlatformNotSupportedException(
                "diCAN has no device discovery for this platform: it reads cfgmgr32 and the "
                + "registry on Windows and the IO registry on macOS, and this is neither. "
                + "Everything above discovery -- the slcan transport, the protocol and "
                + "the UI -- is platform neutral and untouched by this.");
        }

#if DEBUG
        return (new DemoDeviceEnumerator(), new DemoSessionOpener(options.DemoFramesPerSecond));
#else

        throw new InvalidOperationException(
            "The simulator is not part of this build, so the demo branch must be unreachable.");
#endif
    }
}
