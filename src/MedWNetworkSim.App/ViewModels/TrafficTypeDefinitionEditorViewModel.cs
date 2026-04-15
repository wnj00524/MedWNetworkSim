using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class TrafficTypeDefinitionEditorViewModel : ObservableObject
{
    private string name;
    private string description;
    private RoutingPreference routingPreference;
    private AllocationMode allocationMode;
    private RouteChoiceModel routeChoiceModel;
    private FlowSplitPolicy flowSplitPolicy;
    private RouteChoiceSettings routeChoiceSettings;
    private double? capacityBidPerUnit;
    private int? perishabilityPeriods;

    public TrafficTypeDefinitionEditorViewModel(TrafficTypeDefinition definition)
    {
        name = definition.Name;
        description = definition.Description;
        routingPreference = definition.RoutingPreference;
        allocationMode = definition.AllocationMode;
        routeChoiceModel = definition.RouteChoiceModel;
        flowSplitPolicy = definition.FlowSplitPolicy;
        routeChoiceSettings = definition.RouteChoiceSettings;
        capacityBidPerUnit = definition.CapacityBidPerUnit;
        perishabilityPeriods = definition.PerishabilityPeriods;
    }

    public event EventHandler<ValueChangedEventArgs<string>>? NameChanged;

    public string Name
    {
        get => name;
        set
        {
            var oldValue = name;
            if (!SetProperty(ref name, value))
            {
                return;
            }

            NameChanged?.Invoke(this, new ValueChangedEventArgs<string>(oldValue, value));
        }
    }

    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    public RoutingPreference RoutingPreference
    {
        get => routingPreference;
        set => SetProperty(ref routingPreference, value);
    }

    public AllocationMode AllocationMode
    {
        get => allocationMode;
        set
        {
            if (SetProperty(ref allocationMode, value))
            {
                OnPropertyChanged(nameof(AllocationModeLabel));
                OnPropertyChanged(nameof(AllocationModeHelpText));
            }
        }
    }

    public string AllocationModeLabel => GetAllocationModeLabel(AllocationMode);

    public string AllocationModeHelpText => GetAllocationModeHelpText(AllocationMode);

    public double? CapacityBidPerUnit
    {
        get => capacityBidPerUnit;
        set => SetProperty(ref capacityBidPerUnit, value);
    }

    public int? PerishabilityPeriods
    {
        get => perishabilityPeriods;
        set
        {
            if (value.HasValue && value.Value <= 0)
            {
                value = null;
            }

            SetProperty(ref perishabilityPeriods, value);
        }
    }

    public RouteChoiceModel RouteChoiceModel
    {
        get => routeChoiceModel;
        set
        {
            if (SetProperty(ref routeChoiceModel, value))
            {
                OnPropertyChanged(nameof(IsSystemOptimal));
                OnPropertyChanged(nameof(IsStochasticUserResponsive));
            }
        }
    }

    public FlowSplitPolicy FlowSplitPolicy
    {
        get => flowSplitPolicy;
        set => SetProperty(ref flowSplitPolicy, value);
    }

    public bool IsSystemOptimal => RouteChoiceModel == RouteChoiceModel.SystemOptimal;

    public bool IsStochasticUserResponsive => RouteChoiceModel == RouteChoiceModel.StochasticUserResponsive;

    public int MaxCandidateRoutes
    {
        get => routeChoiceSettings.MaxCandidateRoutes;
        set
        {
            if (routeChoiceSettings.MaxCandidateRoutes == value)
            {
                return;
            }

            routeChoiceSettings.MaxCandidateRoutes = value;
            OnPropertyChanged();
        }
    }

    public double Priority
    {
        get => routeChoiceSettings.Priority;
        set
        {
            if (Math.Abs(routeChoiceSettings.Priority - value) <= double.Epsilon)
            {
                return;
            }

            routeChoiceSettings.Priority = value;
            OnPropertyChanged();
        }
    }

    public double InformationAccuracy
    {
        get => routeChoiceSettings.InformationAccuracy;
        set
        {
            if (Math.Abs(routeChoiceSettings.InformationAccuracy - value) <= double.Epsilon)
            {
                return;
            }

            routeChoiceSettings.InformationAccuracy = value;
            OnPropertyChanged();
        }
    }

    public double RouteDiversity
    {
        get => routeChoiceSettings.RouteDiversity;
        set
        {
            if (Math.Abs(routeChoiceSettings.RouteDiversity - value) <= double.Epsilon)
            {
                return;
            }

            routeChoiceSettings.RouteDiversity = value;
            OnPropertyChanged();
        }
    }

    public double CongestionSensitivity
    {
        get => routeChoiceSettings.CongestionSensitivity;
        set
        {
            if (Math.Abs(routeChoiceSettings.CongestionSensitivity - value) <= double.Epsilon)
            {
                return;
            }

            routeChoiceSettings.CongestionSensitivity = value;
            OnPropertyChanged();
        }
    }

    public double RerouteThreshold
    {
        get => routeChoiceSettings.RerouteThreshold;
        set
        {
            if (Math.Abs(routeChoiceSettings.RerouteThreshold - value) <= double.Epsilon)
            {
                return;
            }

            routeChoiceSettings.RerouteThreshold = value;
            OnPropertyChanged();
        }
    }

    public double Stickiness
    {
        get => routeChoiceSettings.Stickiness;
        set
        {
            if (Math.Abs(routeChoiceSettings.Stickiness - value) <= double.Epsilon)
            {
                return;
            }

            routeChoiceSettings.Stickiness = value;
            OnPropertyChanged();
        }
    }

    public int IterationCount
    {
        get => routeChoiceSettings.IterationCount;
        set
        {
            if (routeChoiceSettings.IterationCount == value)
            {
                return;
            }

            routeChoiceSettings.IterationCount = value;
            OnPropertyChanged();
        }
    }

    public bool InternalizeCongestion
    {
        get => routeChoiceSettings.InternalizeCongestion;
        set
        {
            if (routeChoiceSettings.InternalizeCongestion == value)
            {
                return;
            }

            routeChoiceSettings.InternalizeCongestion = value;
            OnPropertyChanged();
        }
    }

    public TrafficTypeDefinition ToModel()
    {
        return new TrafficTypeDefinition
        {
            Name = Name,
            Description = Description,
            RoutingPreference = RoutingPreference,
            AllocationMode = AllocationMode,
            RouteChoiceModel = RouteChoiceModel,
            FlowSplitPolicy = FlowSplitPolicy,
            RouteChoiceSettings = new RouteChoiceSettings
            {
                MaxCandidateRoutes = MaxCandidateRoutes,
                Priority = Priority,
                InformationAccuracy = InformationAccuracy,
                RouteDiversity = RouteDiversity,
                CongestionSensitivity = CongestionSensitivity,
                RerouteThreshold = RerouteThreshold,
                Stickiness = Stickiness,
                IterationCount = IterationCount,
                InternalizeCongestion = InternalizeCongestion
            },
            CapacityBidPerUnit = CapacityBidPerUnit,
            PerishabilityPeriods = PerishabilityPeriods
        };
    }

    public static string GetAllocationModeLabel(AllocationMode allocationMode)
    {
        return allocationMode switch
        {
            AllocationMode.ProportionalBranchDemand => "Split by downstream demand",
            _ => "Greedy best route"
        };
    }

    public static string GetAllocationModeHelpText(AllocationMode allocationMode)
    {
        return allocationMode switch
        {
            AllocationMode.ProportionalBranchDemand => "Split by downstream demand: divides flow across branches in proportion to the total reachable demand beyond each branch.",
            _ => "Greedy best route: sends flow to the current best destination route first."
        };
    }
}