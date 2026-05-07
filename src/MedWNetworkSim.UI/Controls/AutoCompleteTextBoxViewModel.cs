using MedWNetworkSim.Presentation;

namespace MedWNetworkSim.UI.Controls;
/// <summary>
/// Represents a data model for auto complete text box view entities within the simulation.
/// </summary>

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
    /// <summary>
    /// Gets a value indicating whether has active suggestion is enabled or active.
    /// </summary>

    public bool HasActiveSuggestion =>
        !string.IsNullOrWhiteSpace(SelectedSuggestion) &&
        FilteredSuggestions.Any(item => Comparer.Equals(item, SelectedSuggestion));
    /// <summary>
    /// Assigns or updates the suggestions.
    /// </summary>

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
    /// <summary>
    /// Executes the open drop down if available operation.
    /// </summary>

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
    /// <summary>
    /// Executes the close drop down operation.
    /// </summary>

    public void CloseDropDown() => IsDropDownOpen = false;
    /// <summary>
    /// Executes the move selection operation.
    /// </summary>

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
    /// <summary>
    /// Executes the accept selection operation.
    /// </summary>

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
