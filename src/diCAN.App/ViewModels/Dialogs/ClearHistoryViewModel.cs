using DiCAN.App.Localization;

namespace DiCAN.App.ViewModels.Dialogs;

// Manages clear history.
public sealed class ClearHistoryViewModel(ILocalizationService localization, int storedCount)
{

    public int StoredCount { get; } = storedCount;

    public string Message => localization.Format("History.ClearAll.Message", StoredCount);
}
