using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class AllocationModeOptionViewModel(AllocationMode value, string label, string helpText)
{
    public AllocationMode Value { get; } = value;

    public string Label { get; } = label;

    public string HelpText { get; } = helpText;
}
