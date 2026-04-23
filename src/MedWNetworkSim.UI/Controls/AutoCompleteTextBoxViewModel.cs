using MedWNetworkSim.Presentation;

namespace MedWNetworkSim.UI.Controls;

public sealed class AutoCompleteTextBoxViewModel : ObservableObject
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private string text = string.Empty;
    private IReadOnlyList<string> allSuggestions = [];
    private IReadOnlyList<string> filteredSuggestions = [];
    private string? selectedSuggestion;
    private bool isDropDownOpen;

    public string Text
    {
        get => text;
        set
        {
            var normalized = value ?? string.Empty;
            if (!SetProperty(ref text, normalized))
            {
                return;
            }

            UpdateFilter();
        }
    }

    public IReadOnlyList<string> AllSuggestions
    {
        get => allSuggestions;
        private set => SetProperty(ref allSuggestions, value);
    }

    public IReadOnlyList<string> FilteredSuggestions
    {
        get => filteredSuggestions;
        private set => SetProperty(ref filteredSuggestions, value);
    }

    public string? SelectedSuggestion
    {
        get => selectedSuggestion;
        set => SetProperty(ref selectedSuggestion, value);
    }

    public bool IsDropDownOpen
    {
        get => isDropDownOpen;
        set => SetProperty(ref isDropDownOpen, value);
    }

    public bool HasActiveSuggestion =>
        !string.IsNullOrWhiteSpace(SelectedSuggestion) &&
        FilteredSuggestions.Any(item => Comparer.Equals(item, SelectedSuggestion));

    public void SetSuggestions(IEnumerable<string>? suggestions)
    {
        AllSuggestions = (suggestions ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(Comparer)
            .OrderBy(item => item, Comparer)
            .ToList();

        UpdateFilter();
    }

    public void OpenDropDownIfAvailable()
    {
        if (FilteredSuggestions.Count == 0)
        {
            IsDropDownOpen = false;
            return;
        }

        EnsureSelection();
        IsDropDownOpen = true;
    }

    public void CloseDropDown() => IsDropDownOpen = false;

    public void MoveSelection(int offset)
    {
        if (FilteredSuggestions.Count == 0)
        {
            SelectedSuggestion = null;
            IsDropDownOpen = false;
            return;
        }

        var currentIndex = SelectedSuggestion is null
            ? -1
            : FilteredSuggestions
                .Select((item, index) => (item, index))
                .FirstOrDefault(pair => Comparer.Equals(pair.item, SelectedSuggestion))
                .index;

        var nextIndex = currentIndex < 0
            ? (offset >= 0 ? 0 : FilteredSuggestions.Count - 1)
            : Math.Clamp(currentIndex + offset, 0, FilteredSuggestions.Count - 1);

        SelectedSuggestion = FilteredSuggestions[nextIndex];
        IsDropDownOpen = true;
    }

    public bool AcceptSelection()
    {
        EnsureSelection();
        if (SelectedSuggestion is null)
        {
            return false;
        }

        Text = SelectedSuggestion;
        IsDropDownOpen = false;
        return true;
    }

    private void UpdateFilter()
    {
        FilteredSuggestions = string.IsNullOrWhiteSpace(Text)
            ? AllSuggestions
            : AllSuggestions
                .Where(item => item.Contains(Text, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (!HasActiveSuggestion)
        {
            SelectedSuggestion = FilteredSuggestions.FirstOrDefault();
        }

        if (FilteredSuggestions.Count == 0)
        {
            SelectedSuggestion = null;
            IsDropDownOpen = false;
        }
    }

    private void EnsureSelection()
    {
        if (HasActiveSuggestion || FilteredSuggestions.Count == 0)
        {
            return;
        }

        SelectedSuggestion = FilteredSuggestions[0];
    }
}
