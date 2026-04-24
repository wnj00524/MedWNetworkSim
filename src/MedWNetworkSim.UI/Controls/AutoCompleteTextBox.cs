using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System.Windows.Input;

namespace MedWNetworkSim.UI.Controls;

public sealed class AutoCompleteTextBox : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<AutoCompleteTextBox, string?>(
            nameof(Text),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IEnumerable<string>?> SuggestionsProperty =
        AvaloniaProperty.Register<AutoCompleteTextBox, IEnumerable<string>?>(nameof(Suggestions));

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<AutoCompleteTextBox, string?>(nameof(Watermark));

    public static readonly StyledProperty<ICommand?> SubmitCommandProperty =
        AvaloniaProperty.Register<AutoCompleteTextBox, ICommand?>(nameof(SubmitCommand));

    private readonly AutoCompleteTextBoxViewModel viewModel = new();
    private readonly TextBox inputBox;
    private readonly Popup popup;
    private readonly ListBox suggestionList;
    private bool isSynchronizingText;
    private INotifyCollectionChanged? suggestionCollection;

    public AutoCompleteTextBox()
    {
        viewModel.PropertyChanged += HandleViewModelPropertyChanged;

        inputBox = new TextBox
        {
            MinHeight = 40,
            Padding = new Thickness(10, 8),
            BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.InputBorder),
            BorderThickness = new Thickness(1.2),
            Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),
            Background = new SolidColorBrush(AvaloniaDashboardTheme.InputBackground),
            CornerRadius = AvaloniaDashboardTheme.ControlCornerRadius
        };
        inputBox.Bind(TextBox.TextProperty, new Binding(nameof(AutoCompleteTextBoxViewModel.Text), BindingMode.TwoWay) { Source = viewModel });
        inputBox.Bind(TextBox.WatermarkProperty, new Binding(nameof(Watermark)) { Source = this });
        inputBox.KeyDown += OnKeyDown;
        inputBox.GotFocus += OnFocus;
        inputBox.LostFocus += OnLostFocus;
        inputBox.PropertyChanged += OnInputPropertyChanged;

        suggestionList = new ListBox
        {
            MaxHeight = 220,
            SelectionMode = SelectionMode.Single,
            Background = new SolidColorBrush(AvaloniaDashboardTheme.InputBackground),
            BorderThickness = new Thickness(0)
        };
        suggestionList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(AutoCompleteTextBoxViewModel.FilteredSuggestions)) { Source = viewModel });
        suggestionList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(AutoCompleteTextBoxViewModel.SelectedSuggestion), BindingMode.TwoWay) { Source = viewModel });
        suggestionList.PointerPressed += OnSuggestionPressed;

        popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            PlacementTarget = inputBox,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                Background = new SolidColorBrush(AvaloniaDashboardTheme.InputBackground),
                BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.InputBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = suggestionList
            }
        };
        popup.Bind(Popup.IsOpenProperty, new Binding(nameof(AutoCompleteTextBoxViewModel.IsDropDownOpen)) { Source = viewModel });

        Content = new Grid
        {
            Children =
            {
                inputBox,
                popup
            }
        };

        AttachSuggestionsSource(Suggestions);
        viewModel.Text = Text ?? string.Empty;
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IEnumerable<string>? Suggestions
    {
        get => GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public ICommand? SubmitCommand
    {
        get => GetValue(SubmitCommandProperty);
        set => SetValue(SubmitCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty && !isSynchronizingText)
        {
            viewModel.Text = change.GetNewValue<string?>() ?? string.Empty;
        }

        if (change.Property == SuggestionsProperty)
        {
            AttachSuggestionsSource(change.GetNewValue<IEnumerable<string>?>());
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachSuggestionsSource();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFocus(object? sender, GotFocusEventArgs e) => viewModel.OpenDropDownIfAvailable();

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!inputBox.IsFocused)
                {
                    viewModel.CloseDropDown();
                }
            },
            DispatcherPriority.Background);
    }

    private void OnSuggestionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control control || control.DataContext is not string suggestion)
        {
            return;
        }

        viewModel.SelectedSuggestion = suggestion;
        if (viewModel.AcceptSelection())
        {
            inputBox.Focus();
            e.Handled = true;
        }
    }

    private void OnInputPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty && inputBox.IsFocused)
        {
            viewModel.OpenDropDownIfAvailable();
        }

        if (e.Property == BoundsProperty)
        {
            popup.Width = Math.Max(inputBox.Bounds.Width, 160d);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                viewModel.MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                viewModel.MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Tab:
                if (viewModel.IsDropDownOpen && viewModel.HasActiveSuggestion && viewModel.AcceptSelection())
                {
                    e.Handled = true;
                }

                break;

            case Key.Enter:
                var acceptedSelection = viewModel.IsDropDownOpen && viewModel.HasActiveSuggestion && viewModel.AcceptSelection();
                if (TrySubmit())
                {
                    e.Handled = true;
                }
                else if (acceptedSelection)
                {
                    e.Handled = true;
                }

                break;

            case Key.Escape:
                viewModel.CloseDropDown();
                e.Handled = true;
                break;
        }
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AutoCompleteTextBoxViewModel.Text))
        {
            return;
        }

        var normalized = string.IsNullOrEmpty(viewModel.Text) ? string.Empty : viewModel.Text;
        var current = Text ?? string.Empty;
        if (string.Equals(current, normalized, StringComparison.Ordinal))
        {
            return;
        }

        isSynchronizingText = true;
        try
        {
            SetCurrentValue(TextProperty, normalized);
        }
        finally
        {
            isSynchronizingText = false;
        }
    }

    private void AttachSuggestionsSource(IEnumerable<string>? suggestions)
    {
        if (ReferenceEquals(Suggestions, suggestions) && ReferenceEquals(suggestionCollection, suggestions as INotifyCollectionChanged))
        {
            viewModel.SetSuggestions(suggestions);
            return;
        }

        DetachSuggestionsSource();
        suggestionCollection = suggestions as INotifyCollectionChanged;
        if (suggestionCollection is not null)
        {
            suggestionCollection.CollectionChanged += OnSuggestionsCollectionChanged;
        }

        viewModel.SetSuggestions(suggestions);
    }

    private void DetachSuggestionsSource()
    {
        if (suggestionCollection is null)
        {
            return;
        }

        suggestionCollection.CollectionChanged -= OnSuggestionsCollectionChanged;
        suggestionCollection = null;
    }

    private void OnSuggestionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        viewModel.SetSuggestions(Suggestions);

    private bool TrySubmit()
    {
        var command = SubmitCommand;
        if (command is null || !command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }
}
