using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DiCAN.App.Localization;
using DiCAN.App.ViewModels.Dialogs;
using DiCAN.App.Views.Dialogs;
using DiCAN.Core.Recordings;
using DiCAN.Core.Settings;
using Microsoft.Extensions.Logging;

namespace DiCAN.App.Composition;

// Stores export attempt data.
internal readonly record struct ExportAttempt(string Message, long Omitted, bool Refused);

// Manages i export.
internal interface IExportTarget
{
    string Name { get; }

    // Opens write.
    Task<Stream> OpenWriteAsync();

    // Deletes the requested entry.
    Task DeleteAsync();
}

// Manages storage file target.
internal sealed class StorageFileTarget(IStorageFile file) : IExportTarget
{
    public string Name => file.Name;

    // Opens write.
    public Task<Stream> OpenWriteAsync() => file.OpenWriteAsync();

    // Deletes the requested entry.
    public Task DeleteAsync() => file.DeleteAsync();
}

// Exports recorded CAN frames.
internal static class RecordingExportRunner
{

    // Runs the requested operation.
    public static async Task RunAsync(
        Window owner,
        IRecordingStore store,
        Recording recording,
        ILocalizationService localization,
        ISettingsStore? settings)
    {

        var viewModel = new ExportViewModel(recording.Adapter, localization)
        {
            IsWired = true,
        };
        var dialog = new ExportDialog { DataContext = viewModel };

        dialog.Export = async format => await WriteAsync(
            owner, store, recording, format, viewModel, localization, settings);

        await dialog.ShowDialog(owner);
    }

    // Writes output data.
    private static async Task WriteAsync(
        Window owner,
        IRecordingStore store,
        Recording recording,
        ExportFormat format,
        ExportViewModel viewModel,
        ILocalizationService localization,
        ISettingsStore? settings)
    {
        string extension = format == ExportFormat.Csv ? "csv" : "trc";
        ILogger logger = LoggingBootstrap.Current.Factory.CreateLogger("diCAN.Export");

        IStorageFile? target = await owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = localization["Export.Title"],

                SuggestedFileName =
                    SafeFileName(RecordingNaming.ExportName(
                        recording.Adapter, recording.Port, recording.StartedAt))
                    + "." + extension,
                DefaultExtension = extension,

                FileTypeChoices = [FileTypeFor(format, localization)],
                SuggestedStartLocation =
                    await StartFolderAsync(owner.StorageProvider, settings, logger),
                ShowOverwritePrompt = true,
            });

        if (target is null)
        {

            return;
        }

        ExportAttempt attempt = await WriteFileAsync(
            new StorageFileTarget(target),
            format,
            store,
            recording,
            viewModel,
            logger);

        viewModel.ShowOutcome(attempt.Message, attempt.Omitted, attempt.Refused);

        await RememberFolderAsync(settings, target.TryGetLocalPath(), attempt, logger);
    }

    // Writes file.
    internal static async Task<ExportAttempt> WriteFileAsync(
        IExportTarget target,
        ExportFormat format,
        IRecordingStore store,
        Recording recording,
        ExportViewModel viewModel,
        ILogger logger)
    {
        RecordingExportResult result;
        bool opened = false;

        try
        {

            IReadOnlyList<RecordingFilterChange> changes =
                await store.ReadFilterChangesAsync(recording);

            var export = new RecordingExport(recording, changes);

            if (format == ExportFormat.Trc)
            {
                var text = new StringWriter { NewLine = "\r\n" };

                result = TrcRecordingExporter.Write(
                    text, export, ReadAsync(store, recording).ToBlockingEnumerable());

                if (!result.Refused)
                {
                    await using Stream stream = await target.OpenWriteAsync();
                    opened = true;
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    await writer.WriteAsync(text.ToString());
                }
            }
            else
            {
                await using Stream stream = await target.OpenWriteAsync();
                opened = true;
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    NewLine = "\r\n",
                };

                result = CsvRecordingExporter.Write(
                    writer, export, ReadAsync(store, recording).ToBlockingEnumerable());
            }
        }
        catch (Exception error)
        {

            logger.LogError(
                error,
                "Exporting recording {Recording} ({Volume}) as {Format} to {Target} failed.",
                recording.Id,
                recording.Volume,
                format,
                target.Name);

            if (opened)
            {
                await DeletePartialAsync(target, logger);
            }

            return new ExportAttempt(viewModel.FailedMessage, 0, Refused: true);
        }

        return new ExportAttempt(
            result.Refused ? viewModel.RefusedMessage : viewModel.DoneMessage,
            result.Omitted,
            result.Refused);
    }

    // Deletes the requested entry.
    private static async Task DeletePartialAsync(IExportTarget target, ILogger logger)
    {
        try
        {
            await target.DeleteAsync();
            logger.LogInformation("Deleted the partly written export {Target}.", target.Name);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Could not delete the partly written export {Target}.", target.Name);
        }
    }

    // Gets the export file type.
    internal static FilePickerFileType FileTypeFor(ExportFormat format, ILocalizationService localization) =>
        format == ExportFormat.Csv
            ? new FilePickerFileType(localization["Export.FileType.Csv"]) { Patterns = ["*.csv"] }
            : new FilePickerFileType(localization["Export.FileType.Trc"]) { Patterns = ["*.trc"] };

    // Starts folder.
    private static async Task<IStorageFolder?> StartFolderAsync(
        IStorageProvider provider, ISettingsStore? settings, ILogger logger)
    {
        try
        {
            if (await RememberedFolderAsync(settings, logger) is { } remembered &&
                await provider.TryGetFolderFromPathAsync(remembered) is { } folder)
            {
                return folder;
            }

            return await provider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Could not find a folder for the export dialog to open on.");
            return null;
        }
    }

    // Gets the saved folder.
    internal static async Task<string?> RememberedFolderAsync(ISettingsStore? settings, ILogger logger)
    {
        if (settings is null)
        {
            return null;
        }

        try
        {
            string? folder = await settings.LoadTextAsync(TextSettingKeys.LastExportFolder);

            return folder is { Length: > 0 } && await Task.Run(() => Directory.Exists(folder))
                ? folder
                : null;
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Could not read the folder of the last export.");
            return null;
        }
    }

    // Saves the current choice.
    internal static async Task RememberFolderAsync(
        ISettingsStore? settings, string? exportedFile, ExportAttempt attempt, ILogger logger)
    {
        if (settings is null ||
            attempt.Refused ||
            Path.GetDirectoryName(exportedFile) is not { Length: > 0 } folder)
        {
            return;
        }

        try
        {
            await settings.SaveTextAsync(TextSettingKeys.LastExportFolder, folder);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Could not remember the export folder {Folder}.", folder);
        }
    }

    // Reads input data.
    private static IAsyncEnumerable<RecordedFrame> ReadAsync(
        IRecordingStore store, Recording recording) =>
        store.ReadFramesAsync(recording);

    // Gets safe file name.
    private static string SafeFileName(string name)
    {
        var clean = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            clean.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        }

        string result = clean.ToString().Trim();

        return result.Length == 0 ? "recording" : result;
    }
}
