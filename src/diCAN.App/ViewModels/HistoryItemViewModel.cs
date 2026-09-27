using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace DiCAN.App.ViewModels;

// Manages history item.
public sealed class HistoryItemViewModel
{

    // Initializes this instance.
    private HistoryItemViewModel(string text, bool isEntry, ICommand delete)
    {
        Text = text;
        IsEntry = isEntry;
        DeleteCommand = delete;
    }

    public string Text { get; }

    public bool IsEntry { get; }

    public bool IsClearAction => !IsEntry;

    public ICommand DeleteCommand { get; }

    // Creates a history entry.
    internal static HistoryItemViewModel Entry(string text, Func<string, Task> forget) =>
        new(text, isEntry: true, new AsyncRelayCommand(() => forget(text)));

    // Clears all.
    internal static HistoryItemViewModel ClearAll(Func<Task> clear) =>
        new(string.Empty, isEntry: false, new AsyncRelayCommand(clear));
}
