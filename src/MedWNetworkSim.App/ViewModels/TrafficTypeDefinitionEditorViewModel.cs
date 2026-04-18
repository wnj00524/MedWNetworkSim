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
            OnPropertyChanged(nameof(ValidationText));
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
                OnPropertyChanged(nameof(ValidationText));
            }
        }
    }

    public string AllocationModeLabel => GetAllocationModeLabel(AllocationMode);

    public string AllocationModeHelpText => GetAllocationModeHelpText(AllocationMode);

    public string RouteChoiceModelHelpText => RouteChoiceModel switch
    {
        RouteChoiceModel.SystemOptimal => "System-optimal routes balance network-wide efficiency and can internalize congestion when you need centrally managed routing.",
        RouteChoiceModel.StochasticUserResponsive => "Stochastic user-responsive routes let agents react to imperfect information and route diversity over repeated choices.",
        _ => "User-optimal routes follow the best perceived path for the traffic definition."
    };

    public string FlowSplitPolicyHelpText => FlowSplitPolicy switch
    {
        FlowSplitPolicy.MultiPath => "Multi-path split allows flow to use more than one feasible route when the routing model supports it.",
        _ => "Single-path split keeps each movement on one chosen route."
    };

    public double? CapacityBidPerUnit
    {
        get => capacityBidPerUnit;
        set
        {
            if (SetProperty(ref capacityBidPerUnit, value))
            {
                OnPropertyChanged(nameof(ValidationText));
            }
        }
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

            if (SetProperty(ref perishabilityPeriods, value))
            {
                OnPropertyChanged(nameof(ValidationText));
            }
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
                OnPropertyChanged(nameof(RouteChoiceModelHelpText));
            }
        }
    }

    public FlowSplitPolicy FlowSplitPolicy
    {
        get => flowSplitPolicy;
        set
        {
            if (SetProperty(ref flowSplitPolicy, value))
            {
                OnPropertyChanged(nameof(FlowSplitPolicyHelpText));
            }
        }
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
            OnPropertyChanged(nameof(ValidationText));
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
            OnPropertyChanged(nameof(ValidationText));
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
            OnPropertyChanged(nameof(ValidationText));
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
            OnPropertyChanged(nameof(ValidationText));
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
            OnPropertyChanged(nameof(ValidationText));
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
            OnPropertyChanged(nameof(ValidationText));
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
            OnPropertyChanged(nameof(ValidationText));
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
            OnPropertyChanged(nameof(ValidationText));
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

    public string ValidationText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "Traffic type name is required.";
            }

            if (CapacityBidPerUnit is < 0d)
            {
                return "Bid per unit must be zero or greater when it is set.";
            }

            if (PerishabilityPeriods is < 0)
            {
                return "Perishability periods must be zero or greater.";
            }

            if (MaxCandidateRoutes < 1)
            {
                return "Candidate routes must be at least 1.";
            }

            if (Priority < 0d)
            {
                return "Priority must be zero or greater.";
            }

            if (InformationAccuracy < 0d || InformationAccuracy > 1d)
            {
                return "Information accuracy should stay between 0 and 1.";
            }

            if (RouteDiversity < 0d || RouteDiversity > 1d)
            {
                return "Route diversity should stay between 0 and 1.";
            }

            if (CongestionSensitivity < 0d)
            {
                return "Congestion sensitivity must be zero or greater.";
            }

            if (RerouteThreshold < 0d)
            {
                return "Reroute threshold must be zero or greater.";
            }

            if (Stickiness < 0d || Stickiness > 1d)
            {
                return "Stickiness should stay between 0 and 1.";
            }

            if (IterationCount < 1)
            {
                return "Iteration count must be at least 1.";
            }

            return "Traffic settings are ready to use.";
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
