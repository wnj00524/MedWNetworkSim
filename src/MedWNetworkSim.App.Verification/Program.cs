using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;

ScenarioA_LongEdgeBlocksRelaunch();
ScenarioB_CapacityFreesAfterCompletion();
ScenarioC_BidirectionalSharedOccupancy();
ScenarioD_MultiEdgeTransition();
ScenarioE_TimelineUtilisationUsesOccupancy();

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
