using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class UiTerminologyViewModel : ObservableObject
{
    private bool isWorldbuilderMode;

    public bool IsWorldbuilderMode
    {
        get => isWorldbuilderMode;
        set
        {
            if (!SetProperty(ref isWorldbuilderMode, value))
            {
                return;
            }

            RaiseAllLabelsChanged();
        }
    }

    public string Node => IsWorldbuilderMode ? "Place" : "Node";

    public string Nodes => IsWorldbuilderMode ? "Places" : "Nodes";

    public string NodeLower => IsWorldbuilderMode ? "place" : "node";

    public string NodesLower => IsWorldbuilderMode ? "places" : "nodes";

    public string Edge => IsWorldbuilderMode ? "Route" : "Edge";

    public string Edges => IsWorldbuilderMode ? "Routes" : "Edges";

    public string EdgeLower => IsWorldbuilderMode ? "route" : "edge";

    public string EdgesLower => IsWorldbuilderMode ? "routes" : "edges";

    public string TrafficType => IsWorldbuilderMode ? "Good / Flow" : "Traffic Type";

    public string TrafficTypes => IsWorldbuilderMode ? "Goods / Flows" : "Traffic Types";

    public string TrafficTypeLower => IsWorldbuilderMode ? "good / flow" : "traffic type";

    public string TrafficTypesLower => IsWorldbuilderMode ? "goods / flows" : "traffic types";

    public string Producer => IsWorldbuilderMode ? "Source" : "Producer";

    public string Consumer => IsWorldbuilderMode ? "Need" : "Consumer";

    public string Store => IsWorldbuilderMode ? "Stockpile" : "Store";

    public string NodeEditorTitle => $"{Node} Editor";

    public string NodeEditorIntro =>
        $"Choose a {NodeLower}, set any shared transhipment capacity, then choose one of its {TrafficTypeLower}-role entries and set the {TrafficTypeLower} and role from dropdown lists. You can also bulk-apply one {TrafficTypeLower} role to every {NodeLower} from this window.";

    public string NodeDetailsHeader => $"{Node} Details";

    public string AddNodeButton => $"Add {Node}";

    public string AddNodeFromTemplateButton => $"Add {Node} From Template";

    public string NodeEditorTip =>
        $"Tip: select a {NodeLower} on the canvas or in the main {NodeLower} table, then use this window to manage its {TrafficTypeLower}-role entries with dropdowns.";

    public string TrafficRoleEntryHeader => $"{TrafficType} Role Entry";

    public string AddTrafficRoleButton => $"Add {TrafficType} Role";

    public string ApplyTrafficRoleToAllNodesButton => $"Apply To All {Nodes}...";

    public string TrafficTypeEditorTitle => $"{TrafficType} Editor";

    public string TrafficTypeEditorIntro =>
        $"Add, rename, and edit {TrafficTypesLower} in a full-size editor. Changes update the shared network immediately, including routing preference, allocation mode, and capacity bidding defaults.";

    public string AddTrafficTypeButton => $"Add {TrafficType}";

    public string SelectedTrafficTypeHeader => $"Selected {TrafficType}";

    public string TrafficTypeEditorTip =>
        $"Tip: the main bottom editor remains available for quick review, but this window is the recommended place to edit multiple {TrafficTypesLower}.";

    public string EdgeEditorTitle => $"{Edge} Editor";

    public string EdgeEditorIntro =>
        $"Edit multiple network {EdgesLower} in a full-size workspace. Choose endpoints from the current {NodeLower} list and adjust time, cost, capacity, and direction without relying on the compact bottom panel.";

    public string AddEdgeButton => $"Add {Edge}";

    public string SelectedEdgeHeader => $"Selected {Edge}";

    public string EdgeCapacityTip =>
        $"Tip: {EdgeLower} capacity is optional. Leave it blank for unlimited capacity, or set a value to let {TrafficTypesLower} compete using their bid per unit.";

    public string EdgeEditorTip =>
        $"Tip: the bottom {EdgeLower} table is still useful for quick review, but this window is the recommended place to edit multiple {EdgesLower}.";

    public string ToDisplayRoleName(string roleName)
    {
        if (!IsWorldbuilderMode)
        {
            return roleName;
        }

        return roleName
            .Replace(NodeTrafficRoleCatalog.ProducerRole, Producer, StringComparison.Ordinal)
            .Replace(NodeTrafficRoleCatalog.ConsumerRole, Consumer, StringComparison.Ordinal);
    }

    public string ToInternalRoleName(string roleName)
    {
        if (!IsWorldbuilderMode)
        {
            return roleName;
        }

        return roleName
            .Replace(Producer, NodeTrafficRoleCatalog.ProducerRole, StringComparison.Ordinal)
            .Replace(Consumer, NodeTrafficRoleCatalog.ConsumerRole, StringComparison.Ordinal);
    }

    private void RaiseAllLabelsChanged()
    {
        OnPropertyChanged(nameof(Node));
        OnPropertyChanged(nameof(Nodes));
        OnPropertyChanged(nameof(NodeLower));
        OnPropertyChanged(nameof(NodesLower));
        OnPropertyChanged(nameof(Edge));
        OnPropertyChanged(nameof(Edges));
        OnPropertyChanged(nameof(EdgeLower));
        OnPropertyChanged(nameof(EdgesLower));
        OnPropertyChanged(nameof(TrafficType));
        OnPropertyChanged(nameof(TrafficTypes));
        OnPropertyChanged(nameof(TrafficTypeLower));
        OnPropertyChanged(nameof(TrafficTypesLower));
        OnPropertyChanged(nameof(Producer));
        OnPropertyChanged(nameof(Consumer));
        OnPropertyChanged(nameof(Store));
        OnPropertyChanged(nameof(NodeEditorTitle));
        OnPropertyChanged(nameof(NodeEditorIntro));
        OnPropertyChanged(nameof(NodeDetailsHeader));
        OnPropertyChanged(nameof(AddNodeButton));
        OnPropertyChanged(nameof(AddNodeFromTemplateButton));
        OnPropertyChanged(nameof(NodeEditorTip));
        OnPropertyChanged(nameof(TrafficRoleEntryHeader));
        OnPropertyChanged(nameof(AddTrafficRoleButton));
        OnPropertyChanged(nameof(ApplyTrafficRoleToAllNodesButton));
        OnPropertyChanged(nameof(TrafficTypeEditorTitle));
        OnPropertyChanged(nameof(TrafficTypeEditorIntro));
        OnPropertyChanged(nameof(AddTrafficTypeButton));
        OnPropertyChanged(nameof(SelectedTrafficTypeHeader));
        OnPropertyChanged(nameof(TrafficTypeEditorTip));
        OnPropertyChanged(nameof(EdgeEditorTitle));
        OnPropertyChanged(nameof(EdgeEditorIntro));
        OnPropertyChanged(nameof(AddEdgeButton));
        OnPropertyChanged(nameof(SelectedEdgeHeader));
        OnPropertyChanged(nameof(EdgeCapacityTip));
        OnPropertyChanged(nameof(EdgeEditorTip));
    }
}
