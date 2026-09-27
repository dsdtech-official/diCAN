using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiCAN.App.Localization;
using DiCAN.Core.Recordings;
using Microsoft.Extensions.Logging;

namespace DiCAN.App.ViewModels.Dialogs;

// Manages recording list item.
public sealed class RecordingListItemViewModel
{

    // Initializes this instance.
    public RecordingListItemViewModel(
        Recording recording,
        ILocalizationService localization,
        long frameCount = 0,
        bool framesUnreadable = false,
        long fdCount = 0)
    {
        Recording = recording;
        FrameCount = frameCount;
        FramesUnreadable = framesUnreadable;

        Started = recording.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

        Duration = recording.EndedAt is { } ended
            ? Format(ended - recording.StartedAt)
            : localization["Recordings.Unfinished"];

        Scope = localization[
            recording.Scope == RecordingScope.FilteredOnly
                ? "Recordings.Scope.Filtered"
                : "Recordings.Scope.Everything"];

        Frames = framesUnreadable
            ? localization["Recordings.FramesUnreadable"]
            : fdCount > 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    localization["Recordings.FramesWithFd"],
                    frameCount.ToString("N0", CultureInfo.CurrentCulture),
                    fdCount.ToString("N0", CultureInfo.CurrentCulture))
                : frameCount.ToString("N0", CultureInfo.CurrentCulture);

        Device =
            $"{recording.Adapter} · {recording.Port} · "
            + $"{recording.Configuration.NominalBitrate / 1000}k";
    }

    public Recording Recording { get; }

    public string Started { get; }

    public string Duration { get; }

    public string Scope { get; }

    public long FrameCount { get; }

    public bool FramesUnreadable { get; }

    public string Frames { get; }

    public string Device { get; }

    // Formats the requested value.
    private static string Format(TimeSpan span) =>
        span.ToString("h\\:mm\\:ss", CultureInfo.InvariantCulture);
}

// Manages recordings list.
public sealed partial class RecordingsListViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    // Initializes this instance.
    public RecordingsListViewModel(
        ILocalizationService localization,
        IReadOnlyList<RecordingListItemViewModel>? rows = null,
        bool isSample = true,
        IReadOnlyList<UnreadableRecordingVolume>? unreadableVolumes = null,
        DateTime? today = null)
    {
        _localization = localization;
        IsSample = isSample;
        _all = rows ?? Sample(localization);
        Items = new ObservableCollection<RecordingListItemViewModel>(_all);

        UnreadableVolumeLines = (unreadableVolumes ?? Array.Empty<UnreadableRecordingVolume>())
            .Select(volume => localization.Format(
                volume.Problem switch
                {
                    RecordingVolumeProblem.Damaged => "Recordings.Volume.Damaged",
                    RecordingVolumeProblem.WrittenByNewerBuild => "Recordings.Volume.Newer",
                    _ => "Recordings.Volume.Unreadable",
                },
                volume.VolumeKey))
            .ToList();

        DateTime anchor = (today ?? DateTime.Today).Date;
        From = anchor.AddDays(-2);
        To = anchor;

        Query();
    }

    private readonly IReadOnlyList<RecordingListItemViewModel> _all;

    // Loads saved data.
    public static async Task<RecordingsListViewModel> LoadAsync(
        IRecordingStore store, ILocalizationService localization, ILogger logger,
        DateTime? today = null)
    {

        RecordingLibrary library = await store.ListLibraryAsync();
        IReadOnlyList<Recording> recordings = library.Recordings;
        var rows = new List<RecordingListItemViewModel>(recordings.Count);

        foreach (UnreadableRecordingVolume volume in library.UnreadableVolumes)
        {
            logger.LogError(
                volume.Error,
                "The recording volume {Volume} could not be read ({Problem}).",
                volume.VolumeKey,
                volume.Problem);
        }

        foreach (Recording recording in recordings)
        {

            RecordingFrameTally tally;

            try
            {
                tally = await store.CountFramesAsync(recording);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {

                logger.LogError(
                    error,
                    "Counting the frames of recording {Recording} ({Volume}) failed.",
                    recording.Id,
                    recording.Volume);

                rows.Add(new RecordingListItemViewModel(recording, localization, framesUnreadable: true));
                continue;
            }

            long total = tally.Total;

            rows.Add(new RecordingListItemViewModel(recording, localization, total, fdCount: tally.Fd));
        }

        return new RecordingsListViewModel(
            localization, rows, isSample: false, unreadableVolumes: library.UnreadableVolumes,
            today: today)
        {
            LibraryBytes = await store.GetLibraryBytesAsync(),
        };
    }

    public long LibraryBytes { get; private set; }

    public string LibrarySize => _localization.Format("Recordings.LibrarySize", Human(LibraryBytes));

    // Formats a byte count.
    private static string Human(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : bytes >= 1024L * 1024
                ? (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
                : (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " kB";

    public ObservableCollection<RecordingListItemViewModel> Items { get; }

    public bool IsSample { get; }

    [ObservableProperty]
    public partial RecordingListItemViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    public bool IsEmpty => Items.Count == 0 && !HasUnreadableVolumes;

    public bool IsTrulyEmpty => Items.Count == 0 && _all.Count == 0 && !HasUnreadableVolumes;

    public IReadOnlyList<string> UnreadableVolumeLines { get; }

    public bool HasUnreadableVolumes => UnreadableVolumeLines.Count > 0;

    [ObservableProperty]
    public partial DateTime? From { get; set; }

    [ObservableProperty]
    public partial DateTime? To { get; set; }

    public bool IsFilteredEmpty => Items.Count == 0 && _all.Count > 0;

    public bool IsFiltered => From is not null || To is not null;

    public string FilterSummary =>
        _localization.Format("Recordings.Filter.Summary", Items.Count, _all.Count);

    // Queries the requested value.
    [RelayCommand]
    private void Query()
    {
        DateTime? from = From?.Date;
        DateTime? to = To?.Date;

        Items.Clear();

        foreach (RecordingListItemViewModel row in _all)
        {
            DateTime started = row.Recording.StartedAt.Date;

            if (from is { } lower && started < lower)
            {
                continue;
            }

            if (to is { } upper && started > upper)
            {
                continue;
            }

            Items.Add(row);
        }

        if (Selected is { } selected && !Items.Contains(selected))
        {
            Selected = null;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsTrulyEmpty));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(FilterSummary));
    }

    // Handles selected changed.
    partial void OnSelectedChanged(RecordingListItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));

        DeleteArmed = false;

        DeleteFailed = false;
    }

    [ObservableProperty]
    public partial bool DeleteArmed { get; set; }

    [ObservableProperty]
    public partial bool DeleteFailed { get; set; }

    public string DeleteFailedMessage => _localization["Recordings.Delete.Failed"];

    public string DeleteWarning => Selected is { } row
        ? row.FramesUnreadable
            ? _localization.Format("Recordings.Delete.ConfirmNoCount", row.Device, row.Started)
            : _localization.Format("Recordings.Delete.Confirm", row.Device, row.FrameCount, row.Started)
        : string.Empty;

    // Handles delete armed changed.
    partial void OnDeleteArmedChanged(bool value) => OnPropertyChanged(nameof(DeleteWarning));

    // Deletes the requested entry.
    public async Task<bool> DeleteSelectedAsync(IRecordingStore store, ILogger logger)
    {
        if (Selected is not { } row)
        {
            return false;
        }

        try
        {
            await store.DeleteAsync(row.Recording);
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(
                error,
                "Deleting recording {Recording} ({Volume}) failed.",
                row.Recording.Id,
                row.Recording.Volume);

            DeleteArmed = false;
            DeleteFailed = true;
            return false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompact))]
    public partial bool IsRecordingInProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompact))]
    public partial bool CompactBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompactMessage))]
    public partial string? CompactMessage { get; set; }

    public bool HasCompactMessage => !string.IsNullOrEmpty(CompactMessage);

    public bool CanCompact => !IsRecordingInProgress && !CompactBusy && !IsSample;

    public string CompactBlockedReason => _localization["Recordings.Compact.WhileRecording"];

    // Compacts stored data.
    public async Task CompactAsync(IRecordingStore store, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!CanCompact)
        {
            return;
        }

        CompactBusy = true;
        CompactMessage = null;

        try
        {
            RecordingCompactResult result = await store.CompactAsync();

            LibraryBytes = result.BytesAfter;
            OnPropertyChanged(nameof(LibrarySize));

            CompactMessage = result.NothingToCompact
                ? _localization["Recordings.Compact.Nothing"]
                : result.VolumesSkipped > 0
                    ? _localization.Format(
                        "Recordings.Compact.DoneSkipped",
                        Human(result.BytesReclaimed), result.VolumesCompacted, result.VolumesSkipped)
                    : _localization.Format(
                        "Recordings.Compact.Done",
                        Human(result.BytesReclaimed), result.VolumesCompacted);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Compacting the recording library failed.");

            CompactMessage = _localization["Recordings.Compact.Failed"];
        }
        finally
        {
            CompactBusy = false;
        }
    }

    // Creates sample display data.
    private static List<RecordingListItemViewModel> Sample(ILocalizationService localization)
    {
        var started = new DateTimeOffset(2026, 8, 22, 14, 30, 0, TimeSpan.FromHours(8));

        // Gets the indexed item.
        Recording At(long id, string name, DateTimeOffset at, TimeSpan? length, bool raw,
            RecordingScope scope) =>
            new(
                id,
                $"{at.Year:D4}-{at.Month:D2}",
                name,
                at,
                length is { } span ? at + span : null,
                "SN0001",
                Core.Protocol.SlcanFirmwareGeneration.Elmue25,
                "COM8",
                new Core.Transport.CanBusConfiguration(500_000, null, Core.Protocol.SlcanOpenMode.Normal),
                scope,
                raw,
                "b1234",
                "1.0.0");

        return
        [
            new RecordingListItemViewModel(
                At(3, "Station 3 2026-08-22 1430", started, TimeSpan.FromMinutes(12), false,
                    RecordingScope.FilteredOnly),
                localization),

            new RecordingListItemViewModel(
                At(2, "Station 3 2026-08-22 0905", started.AddHours(-5), TimeSpan.FromHours(2), true,
                    RecordingScope.Everything),
                localization,
                frameCount: 8_192,
                fdCount: 128),

            new RecordingListItemViewModel(
                At(1, "COM8 2026-07-30 1712", started.AddDays(-23), TimeSpan.FromMinutes(4), false,
                    RecordingScope.FilteredOnly),
                localization),
            new RecordingListItemViewModel(
                At(4, "Station 3 2026-08-22 1651", started.AddHours(2), null, false,
                    RecordingScope.FilteredOnly),
                localization),
        ];
    }
}
