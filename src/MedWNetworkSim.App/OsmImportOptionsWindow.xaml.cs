using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MedWNetworkSim.App.Import;

namespace MedWNetworkSim.App;

public partial class OsmImportOptionsWindow : Window
{
    private bool isUpdatingValue;

    public OsmImportOptionsWindow()
    {
        InitializeComponent();
        UpdateEstimatedText(25);
        EnableRetentionCheckBox_OnChanged(this, new RoutedEventArgs());
    }

    public OsmImportOptions? ImportOptions { get; private set; }

    private void EnableRetentionCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        var enabled = EnableRetentionCheckBox.IsChecked == true;
        RetentionSlider.IsEnabled = enabled;
        RetentionTextBox.IsEnabled = enabled;
        StrategyComboBox.IsEnabled = enabled;
        AlwaysKeepNamedTransitionsCheckBox.IsEnabled = enabled;
        PreserveShapeOnLongSegmentsCheckBox.IsEnabled = enabled;
        UpdateImportSummary();
    }

    private void RetentionValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isUpdatingValue)
        {
            return;
        }

        if (RetentionTextBox is null)
        {
            return;
        }

        isUpdatingValue = true;
        var value = (int)Math.Round(e.NewValue, MidpointRounding.AwayFromZero);
        RetentionTextBox.Text = value.ToString(CultureInfo.InvariantCulture);
        UpdateEstimatedText(value);
        isUpdatingValue = false;
    }

    private void RetentionTextBoxOnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (isUpdatingValue)
        {
            return;
        }

        if (!int.TryParse(RetentionTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return;
        }

        value = Math.Clamp(value, 1, 100);
        isUpdatingValue = true;
        RetentionSlider.Value = value;
        UpdateEstimatedText(value);
        isUpdatingValue = false;
    }

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        var percentage = (int)Math.Round(RetentionSlider.Value, MidpointRounding.AwayFromZero);
        var strategy = StrategyComboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<OsmRetentionStrategy>(item.Tag?.ToString(), out var parsed)
            ? parsed
            : OsmRetentionStrategy.Balanced;

        ImportOptions = new OsmImportOptions(
            EnableRetentionCheckBox.IsChecked == true,
            percentage,
            strategy,
            AlwaysKeepNamedTransitionsCheckBox.IsChecked != false,
            PreserveShapeOnLongSegmentsCheckBox.IsChecked != false);

        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateEstimatedText(int percentage)
    {
        if (EstimatedCountText is null)
        {
            return;
        }

        EstimatedCountText.Text = $"Estimated retained node count: approximately {percentage}% of parsed OSM road nodes (final exact count shown after parsing).";
        UpdateImportSummary();
    }

    private void UpdateImportSummary()
    {
        if (ImportSummaryText is null)
        {
            return;
        }

        if (EnableRetentionCheckBox.IsChecked != true)
        {
            ImportSummaryText.Text = "The importer will keep all parsed road nodes and use the full road detail available in the file.";
            return;
        }

        var strategyLabel = StrategyComboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? "Balanced"
            : "Balanced";
        var percentage = (int)Math.Round(RetentionSlider.Value, MidpointRounding.AwayFromZero);
        ImportSummaryText.Text = $"The importer will target roughly {percentage}% retention using the {strategyLabel} strategy.";
    }
}
