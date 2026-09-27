using System.Runtime.InteropServices;
using DiCAN.Infrastructure.Devices;
using DiCAN.Windows.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Compact;

namespace DiCAN.App.Composition;

// Stores logging runtime data.
public sealed record LoggingRuntime(
    ILoggerFactory Factory, CommandLineOptions Options, string? LogDirectory)
{

    // Gets disabled.
    public static LoggingRuntime Disabled() =>
        new(NullLoggerFactory.Instance, CommandLineOptions.Default, null);
}

// Manages logging bootstrap.
public static class LoggingBootstrap
{

    public static LoggingRuntime Current { get; private set; } = LoggingRuntime.Disabled();

    // Closes application services.
    public static void Shutdown()
    {
        LoggingRuntime runtime = Current;
        Current = LoggingRuntime.Disabled();

        (runtime.Factory as IDisposable)?.Dispose();
    }

    // Registers exception handlers.
    public static void HookUnhandledExceptions()
    {

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            RecordCrash(Current.Factory, Shutdown, e.ExceptionObject, e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) => RecordUnobserved(Current.Factory, e);
    }

    // Records crash.
    internal static void RecordCrash(
        ILoggerFactory factory, Action flush, object? exceptionObject, bool terminating)
    {
        Microsoft.Extensions.Logging.ILogger logger = factory.CreateLogger("diCAN.Crash");

        if (exceptionObject is Exception exception)
        {
            logger.LogCritical(exception, "Unhandled exception, terminating={Terminating}.", terminating);
        }
        else
        {
            logger.LogCritical(
                "Unhandled throw of a non-exception {Type}, terminating={Terminating}.",
                exceptionObject?.GetType().FullName ?? "(null)",
                terminating);
        }

        if (terminating)
        {
            flush();
        }
    }

    // Records unobserved.
    internal static void RecordUnobserved(ILoggerFactory factory, UnobservedTaskExceptionEventArgs e)
    {
        factory.CreateLogger("diCAN.Crash")
            .LogError(e.Exception, "A background task failed and nothing was waiting for it.");

        e.SetObserved();
    }

    // Initializes the component.
    public static LoggingRuntime Initialize(string[]? args)
    {
        CommandLineOptions options = CommandLineOptions.Parse(args);

        string? directory = TryCreateLogDirectory(options.DataDirectory);

        LoggerConfiguration configuration = new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilogLevel(options.LogLevel))

            .MinimumLevel.Override("Avalonia", LogEventLevel.Warning);

        if (directory is not null)
        {
            configuration = configuration.WriteTo.Async(sink => sink.File(
                new CompactJsonFormatter(),
                Path.Combine(directory, "dican-.jsonl"),
                rollingInterval: RollingInterval.Day,

                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 32L * 1024 * 1024,
                rollOnFileSizeLimit: true,

                flushToDiskInterval: TimeSpan.FromSeconds(2)));
        }

        Serilog.Core.Logger serilog = configuration.CreateLogger();
        var factory = new SerilogLoggerFactory(serilog, dispose: true);

        Current = new LoggingRuntime(factory, options, directory);

        WriteBanner(factory.CreateLogger("diCAN"), options, directory);

        return Current;
    }

    // Writes banner.
    private static void WriteBanner(
        Microsoft.Extensions.Logging.ILogger logger, CommandLineOptions options, string? directory)
    {

        if (options.Demo)
        {
            logger.LogWarning(
                "SIMULATED BUS: this run was started with --demo. No frame in this log came off a "
                + "real adapter.");
        }
        else if (options.DemoRefused)
        {

            logger.LogWarning(
                "--demo was requested, but the simulator is not compiled into this build. "
                + "Running against real hardware.");
        }

        if (options.DataDirectoryRefused)
        {
            logger.LogWarning(
                "--data-dir was given, but this build does not support it. Files are in %AppData%.");
        }
        else if (options.DataDirectoryUnusable)
        {
            logger.LogWarning("--data-dir was given without a usable path. Files are in %AppData%.");
        }

        logger.LogInformation(
            "diCAN {Version} starting. started={Started} runtime.rid={Rid} "
            + "process.architecture={Arch} os={Os} logs={Logs}",
            typeof(LoggingBootstrap).Assembly.GetName().Version?.ToString() ?? "unknown",
            options.Describe(),
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.ProcessArchitecture,
            RuntimeInformation.OSDescription,
            directory ?? "(none: no writable directory)");
    }

    // Logs directory for.
    internal static string? LogDirectoryFor(string? root)
    {
        string resolved = root ?? Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        return resolved.Length == 0 ? null : Path.Combine(resolved, DataFolderName, "logs");
    }

    internal static string DataFolderName { get; } = DataFolder.NameFor(
        OperatingSystem.IsWindows() ? PackageIdentity.CurrentFamilyName() : null);

    // Tries create log directory.
    internal static string? TryCreateLogDirectory(string? root)
    {
        try
        {
            string? directory = LogDirectoryFor(root);

            if (directory is null)
            {
                return null;
            }

            Directory.CreateDirectory(directory);

            return directory;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // Maps the logging level.
    private static LogEventLevel ToSerilogLevel(string? level) => level?.ToLowerInvariant() switch
    {
        "off" => LogEventLevel.Fatal,
        "error" => LogEventLevel.Error,
        "warning" => LogEventLevel.Warning,
        "info" => LogEventLevel.Information,
        "debug" => LogEventLevel.Debug,
        "trace" => LogEventLevel.Verbose,
        _ => LogEventLevel.Information,
    };
}
