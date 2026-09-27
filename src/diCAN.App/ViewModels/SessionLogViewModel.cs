using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiCAN.App.Localization;
using DiCAN.Core.Diagnostics;

namespace DiCAN.App.ViewModels;

// Stores session log row data.
public readonly record struct SessionLogRow(string Time, string Glyph, bool IsProblem, string Message);

// Manages session log.
public sealed partial class SessionLogViewModel : ObservableObject
{
    private readonly SessionJournal _journal;
    private readonly ILocalizationService _localization;

    // Initializes this instance.
    public SessionLogViewModel(SessionJournal journal, ILocalizationService localization)
    {
        _journal = journal;
        _localization = localization;

        _journal.Changed += (_, _) => Rebuild();

        Rebuild();
    }

    public const int RenderLimit = 100;

    public ObservableCollection<SessionLogRow> Rows { get; } = [];

    private int _matched;

    [ObservableProperty]
    public partial bool ProblemsOnly { get; set; }

    public string Summary
    {
        get
        {

            bool shortened = _journal.Truncated || _matched > Rows.Count;

            string key = ProblemsOnly
                ? "Log.SummaryFiltered"
                : shortened ? "Log.SummaryTruncated" : "Log.Summary";

            return ProblemsOnly
                ? _localization.Format(key, Rows.Count, _journal.Entries.Count)
                : _localization.Format(key, Rows.Count);
        }
    }

    // Saves header.
    public string SaveHeader(string? logFolder) =>
        $"diCAN session log · {Rows.Count} of {_journal.Entries.Count} event(s)"
        + (ProblemsOnly ? " · warnings and errors only" : string.Empty)
        + (logFolder is { Length: > 0 } folder ? $"{Environment.NewLine}Log files: {folder}" : string.Empty)
        + Environment.NewLine + Environment.NewLine;

    [ObservableProperty]
    public partial SessionLogRow? SelectedRow { get; set; }

    public bool HasSelectedRow => SelectedRow is not null;

    // Handles selected row changed.
    partial void OnSelectedRowChanged(SessionLogRow? value) =>
        OnPropertyChanged(nameof(HasSelectedRow));

    public bool IsEmpty => Rows.Count == 0;

    public string EmptyText => _localization[
        ProblemsOnly ? "Log.NoProblems" : "Log.Empty"];

    // Copies text.
    public string CopyText()
    {
        StringBuilder text = new();

        foreach (SessionLogRow row in Rows)
        {
            text.Append(row.Time).Append("  ").AppendLine(row.Message);
        }

        return text.ToString();
    }

    // Copies selected text.
    public string CopySelectedText() =>
        SelectedRow is { } row ? $"{row.Time}  {row.Message}" : string.Empty;

    // Clears the stored state.
    [RelayCommand]
    private void Clear()
    {
        _journal.Clear();
        Rebuild();
    }

    // Handles problems only changed.
    partial void OnProblemsOnlyChanged(bool value) => Rebuild();

    // Rebuilds the displayed data.
    private void Rebuild()
    {
        Rows.Clear();

        int matched = 0;

        foreach (SessionEvent candidate in _journal.Entries)
        {
            if (!ProblemsOnly || candidate.Level is SessionEventLevel.Warning or SessionEventLevel.Error)
            {
                matched++;
            }
        }

        _matched = matched;
        int skip = Math.Max(0, matched - RenderLimit);
        int seen = 0;

        foreach (SessionEvent entry in _journal.Entries)
        {
            bool problem = entry.Level is SessionEventLevel.Warning or SessionEventLevel.Error;

            if (ProblemsOnly && !problem)
            {
                continue;
            }

            if (seen++ < skip)
            {
                continue;
            }

            Rows.Add(new SessionLogRow(
                Format(entry.At),
                entry.Level switch
                {

                    SessionEventLevel.Error => "x",
                    SessionEventLevel.Warning => "!",
                    _ => "·",
                },
                problem,

                entry.Count > 1
                    ? string.Create(CultureInfo.InvariantCulture, $"{entry.Message} × {entry.Count}")
                    : entry.Message));
        }

        SelectedRow = null;

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(HasSelectedRow));
    }

    // Formats the requested value.
    private static string Format(TimeSpan since)
    {
        if (since < TimeSpan.Zero)
        {
            since = TimeSpan.Zero;
        }

        return $"{(int)since.TotalMinutes:00}:{since.Seconds:00}.{since.Milliseconds:000}";
    }
}
