using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

ScenarioA_LongEdgeBlocksRelaunch();
ScenarioB_CapacityFreesAfterCompletion();
ScenarioC_BidirectionalSharedOccupancy();
ScenarioD_MultiEdgeTransition();
ScenarioE_TimelineUtilisationUsesOccupancy();
ScenarioF_LoopedProductionWindows();
ScenarioG_MultipleProductionWindows();
ScenarioH_StrictPrecursorGating();
ScenarioI_RatioLimitedProduction();
ScenarioJ_MultiplePrecursorLimitingInput();
ScenarioK_NoPrecursorLegacyProduction();
ScenarioL_CliGuiOverride();
ScenarioM_LegacySingleWindowLoadCompatibility();

Console.WriteLine("Temporal occupancy verification passed.");

static void ScenarioA_LongEdgeBlocksRelaunch()
{
    var network = CreateNetwork(edgeTime: 3d, bidirectional: false);
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);

    var period1 = engine.Advance(network, state);
    var period2 = engine.Advance(network, state);
    var period3 = engine.Advance(network, state);

    AssertEqual(100d, period1.Allocations.Sum(allocation => allocation.Quantity), "A period 1 allocation");
    AssertEqual(0d, period2.Allocations.Sum(allocation => allocation.Quantity), "A period 2 allocation");
    AssertEqual(0d, period3.Allocations.Sum(allocation => allocation.Quantity), "A period 3 allocation");
    AssertAtMost(100d, period1.EdgeOccupancy.GetValueOrDefault("AB"), "A period 1 occupancy");
    AssertAtMost(100d, period2.EdgeOccupancy.GetValueOrDefault("AB"), "A period 2 occupancy");
    AssertAtMost(100d, period3.EdgeOccupancy.GetValueOrDefault("AB"), "A period 3 occupancy");
}

static void ScenarioB_CapacityFreesAfterCompletion()
{
    var network = CreateNetwork(edgeTime: 1d, bidirectional: false);
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);

    var period1 = engine.Advance(network, state);
    var period2 = engine.Advance(network, state);

    AssertEqual(100d, period1.Allocations.Sum(allocation => allocation.Quantity), "B period 1 allocation");
    AssertEqual(100d, period2.Allocations.Sum(allocation => allocation.Quantity), "B period 2 allocation");
    AssertEqual(0d, state.OccupiedEdgeCapacity.GetValueOrDefault("AB"), "B durable occupancy after completion");
}

static void ScenarioC_BidirectionalSharedOccupancy()
{
    var network = CreateNetwork(edgeTime: 2d, bidirectional: true, production: 60d, consumption: 60d);
    network.Nodes[0].TrafficProfiles[0].ProductionEndPeriod = 1;
    network.Nodes[0].TrafficProfiles[0].Consumption = 100d;
    network.Nodes[0].TrafficProfiles[0].ConsumptionStartPeriod = 2;
    network.Nodes[1].TrafficProfiles[0].ConsumptionEndPeriod = 1;
    network.Nodes[1].TrafficProfiles[0].Production = 100d;
    network.Nodes[1].TrafficProfiles[0].ProductionStartPeriod = 2;

    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);

    var period1 = engine.Advance(network, state);
    var period2 = engine.Advance(network, state);

    AssertEqual(60d, period1.Allocations.Sum(allocation => allocation.Quantity), "C forward launch");
    AssertEqual(40d, period2.Allocations.Sum(allocation => allocation.Quantity), "C shared remaining reverse launch");
    AssertEqual(100d, period2.EdgeOccupancy.GetValueOrDefault("AB"), "C shared occupancy");
    AssertEqual(40d, period2.EdgeFlows.GetValueOrDefault("AB").ReverseQuantity, "C reverse flow");
}

static void ScenarioD_MultiEdgeTransition()
{
    var network = CreateNetwork(edgeTime: 1d, bidirectional: false);
    network.Nodes.Insert(1, new NodeModel
    {
        Id = "B",
        Name = "B",
        TrafficProfiles =
        [
            new NodeTrafficProfile
            {
                TrafficType = "med",
                CanTransship = true
            }
        ]
    });
    network.Edges =
    [
        new EdgeModel { Id = "AB", FromNodeId = "A", ToNodeId = "B", Time = 1d, Cost = 1d, Capacity = 100d, IsBidirectional = false },
        new EdgeModel { Id = "BC", FromNodeId = "B", ToNodeId = "C", Time = 2d, Cost = 1d, Capacity = 100d, IsBidirectional = false }
    ];

    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);
    var period1 = engine.Advance(network, state);

    AssertEqual(100d, period1.EdgeOccupancy.GetValueOrDefault("AB"), "D period occupancy on first edge");
    AssertEqual(0d, state.OccupiedEdgeCapacity.GetValueOrDefault("AB"), "D released first edge after transition");
    AssertEqual(100d, state.OccupiedEdgeCapacity.GetValueOrDefault("BC"), "D claimed second edge after transition");
}

static void ScenarioE_TimelineUtilisationUsesOccupancy()
{
    var network = CreateNetwork(edgeTime: 3d, bidirectional: false);
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);

    var results = Enumerable.Range(0, 3)
        .Select(_ => engine.Advance(network, state))
        .ToList();

    foreach (var result in results)
    {
        var utilisation = result.EdgeOccupancy.GetValueOrDefault("AB") / network.Edges[0].Capacity!.Value;
        AssertAtMost(1d, utilisation, $"E period {result.Period} utilisation");
    }
}

static void ScenarioF_LoopedProductionWindows()
{
    var network = CreateProductionOnlyNetwork(1d);
    network.TimelineLoopLength = 12;
    network.Nodes[0].TrafficProfiles[0].ProductionWindows =
    [
        new PeriodWindow { StartPeriod = 1, EndPeriod = 3 }
    ];

    var activePeriods = GetProductionDeltaPeriods(network, 16, "med");
    AssertSequenceEqual([1, 2, 3, 13, 14, 15], activePeriods, "F looped production windows");
}

static void ScenarioG_MultipleProductionWindows()
{
    var network = CreateProductionOnlyNetwork(1d);
    network.Nodes[0].TrafficProfiles[0].ProductionWindows =
    [
        new PeriodWindow { StartPeriod = 1, EndPeriod = 2 },
        new PeriodWindow { StartPeriod = 5, EndPeriod = 6 }
    ];

    var activePeriods = GetProductionDeltaPeriods(network, 6, "med");
    AssertSequenceEqual([1, 2, 5, 6], activePeriods, "G multiple production windows");
}

static void ScenarioH_StrictPrecursorGating()
{
    var (network, state) = CreateBakeryNetwork(wheat: 1d, water: 1d);
    var engine = new TemporalNetworkSimulationEngine();
    engine.Advance(network, state);

    AssertEqual(0d, GetAvailableSupply(state, "Bakery", "Bread"), "H bread output");
    AssertEqual(1d, GetAvailableSupply(state, "Bakery", "Wheat"), "H wheat remains");
    AssertEqual(1d, GetAvailableSupply(state, "Bakery", "Water"), "H water remains");
}

static void ScenarioI_RatioLimitedProduction()
{
    var (network, state) = CreateBakeryNetwork(wheat: 6d, water: 14d);
    var engine = new TemporalNetworkSimulationEngine();
    engine.Advance(network, state);

    AssertEqual(6d, GetAvailableSupply(state, "Bakery", "Bread"), "I bread output");
    AssertEqual(0d, GetAvailableSupply(state, "Bakery", "Wheat"), "I wheat deducted");
    AssertEqual(2d, GetAvailableSupply(state, "Bakery", "Water"), "I water deducted");
}

static void ScenarioJ_MultiplePrecursorLimitingInput()
{
    var (network, state) = CreateBakeryNetwork(wheat: 8d, water: 6d);
    var engine = new TemporalNetworkSimulationEngine();
    engine.Advance(network, state);

    AssertEqual(3d, GetAvailableSupply(state, "Bakery", "Bread"), "J bread output");
    AssertEqual(5d, GetAvailableSupply(state, "Bakery", "Wheat"), "J wheat deducted");
    AssertEqual(0d, GetAvailableSupply(state, "Bakery", "Water"), "J water deducted");
}

static void ScenarioK_NoPrecursorLegacyProduction()
{
    var network = CreateProductionOnlyNetwork(10d);
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);
    engine.Advance(network, state);

    AssertEqual(10d, GetAvailableSupply(state, "Source", "med"), "K legacy production");
}

static void ScenarioL_CliGuiOverride()
{
    var service = new CommandLineRunService();
    if (service.ShouldRunFromCommandLine(["--gui", "run", "--file", "demo.json"]))
    {
        throw new InvalidOperationException("L CLI GUI override should bypass command-line mode.");
    }
}

static void ScenarioM_LegacySingleWindowLoadCompatibility()
{
    const string json = """
{
  "name": "Legacy",
  "nodes": [
    {
      "id": "A",
      "trafficProfiles": [
        {
          "trafficType": "med",
          "production": 1,
          "productionStartPeriod": 2,
          "productionEndPeriod": 3
        }
      ]
    }
  ],
  "edges": []
}
""";
    var network = new NetworkFileService().LoadJson(json);
    var profile = network.Nodes[0].TrafficProfiles[0];
    AssertEqual(1d, profile.ProductionWindows.Count, "M normalized window count");
    AssertEqual(2d, profile.ProductionWindows[0].StartPeriod ?? -1, "M normalized window start");
    AssertEqual(3d, profile.ProductionWindows[0].EndPeriod ?? -1, "M normalized window end");
}

static NetworkModel CreateNetwork(double edgeTime, bool bidirectional, double production = 100d, double consumption = 100d)
{
    return new NetworkModel
    {
        Name = "Verification network",
        TrafficTypes = [new TrafficTypeDefinition { Name = "med", RoutingPreference = RoutingPreference.TotalCost }],
        Nodes =
        [
            new NodeModel
            {
                Id = "A",
                Name = "A",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med", Production = production }]
            },
            new NodeModel
            {
                Id = "C",
                Name = "C",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med", Consumption = consumption }]
            }
        ],
        Edges =
        [
            new EdgeModel
            {
                Id = "AB",
                FromNodeId = "A",
                ToNodeId = "C",
                Time = edgeTime,
                Cost = 1d,
                Capacity = 100d,
                IsBidirectional = bidirectional
            }
        ]
    };
}

static NetworkModel CreateProductionOnlyNetwork(double production)
{
    return new NetworkModel
    {
        Name = "Production only",
        TrafficTypes = [new TrafficTypeDefinition { Name = "med", RoutingPreference = RoutingPreference.TotalCost }],
        Nodes =
        [
            new NodeModel
            {
                Id = "Source",
                Name = "Source",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med", Production = production }]
            }
        ]
    };
}

static (NetworkModel Network, TemporalNetworkSimulationEngine.TemporalSimulationState State) CreateBakeryNetwork(double wheat, double water)
{
    var network = new NetworkModel
    {
        Name = "Bakery",
        TrafficTypes =
        [
            new TrafficTypeDefinition { Name = "Bread", RoutingPreference = RoutingPreference.TotalCost },
            new TrafficTypeDefinition { Name = "Wheat", RoutingPreference = RoutingPreference.TotalCost },
            new TrafficTypeDefinition { Name = "Water", RoutingPreference = RoutingPreference.TotalCost }
        ],
        Nodes =
        [
            new NodeModel
            {
                Id = "Bakery",
                Name = "Bakery",
                TrafficProfiles =
                [
                    new NodeTrafficProfile
                    {
                        TrafficType = "Bread",
                        Production = 10d,
                        InputRequirements =
                        [
                            new ProductionInputRequirement { TrafficType = "Wheat", QuantityPerOutputUnit = 1d },
                            new ProductionInputRequirement { TrafficType = "Water", QuantityPerOutputUnit = 2d }
                        ]
                    },
                    new NodeTrafficProfile { TrafficType = "Wheat" },
                    new NodeTrafficProfile { TrafficType = "Water" }
                ]
            }
        ]
    };

    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);
    state.GetOrCreateNodeTrafficState("Bakery", "Wheat").AvailableSupply = wheat;
    state.GetOrCreateNodeTrafficState("Bakery", "Water").AvailableSupply = water;
    return (network, state);
}

static IReadOnlyList<int> GetProductionDeltaPeriods(NetworkModel network, int periods, string trafficType)
{
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);
    var activePeriods = new List<int>();
    var previousSupply = 0d;

    for (var index = 0; index < periods; index++)
    {
        var result = engine.Advance(network, state);
        var currentSupply = GetAvailableSupply(state, "Source", trafficType);
        if (currentSupply - previousSupply > 0.000001d)
        {
            activePeriods.Add(result.Period);
        }

        previousSupply = currentSupply;
    }

    return activePeriods;
}

static double GetAvailableSupply(
    TemporalNetworkSimulationEngine.TemporalSimulationState state,
    string nodeId,
    string trafficType)
{
    return state.NodeStates.TryGetValue(new TemporalNetworkSimulationEngine.TemporalNodeTrafficKey(nodeId, trafficType), out var nodeState)
        ? nodeState.AvailableSupply
        : 0d;
}

static void AssertEqual(double expected, double actual, string label)
{
    if (Math.Abs(expected - actual) > 0.000001d)
    {
        throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}.");
    }
}

static void AssertAtMost(double expectedMax, double actual, string label)
{
    if (actual > expectedMax + 0.000001d)
    {
        throw new InvalidOperationException($"{label}: expected at most {expectedMax}, actual {actual}.");
    }
}

static void AssertSequenceEqual(IReadOnlyList<int> expected, IReadOnlyList<int> actual, string label)
{
    if (expected.Count != actual.Count || expected.Where((item, index) => item != actual[index]).Any())
    {
        throw new InvalidOperationException($"{label}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}].");
    }
}
