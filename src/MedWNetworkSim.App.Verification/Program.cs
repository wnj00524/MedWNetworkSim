using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.Services;
using MedWNetworkSim.App.ViewModels;

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
ScenarioK_FractionalPrecursorProduction();
ScenarioK_NoPrecursorLegacyProduction();
ScenarioL_CliGuiOverride();
ScenarioM_LegacySingleWindowLoadCompatibility();
ScenarioN_DotnetRunProjectArgumentDoesNotEnterCli();
ScenarioO_WaitingOnBlockedNextEdge();
ScenarioP_RetryAfterCapacityFrees();
ScenarioQ_BlockedByTranshipmentCapacity();
ScenarioR_NoOrphanedOrDoubleCountedWaitingOccupancy();
ScenarioS_StaticRecipeProcurementWithoutInputConsumerProfile();
ScenarioT_TemporalRecipeProcurementWithoutInputConsumerProfile();
ScenarioU_TemporalRecipeProcurementUsesLocalStockFirst();
ScenarioUV_TemporalRecipeOutputInheritsLandedInputCost();
ScenarioUA_TemporalStoreConsumersReplenishAfterRecurringConsumption();
ScenarioUB_NonStoreTemporalDemandBacklogStillAccumulates();
ScenarioUC_RecipeInputStoresStillFeedProduction();
ScenarioV_ProportionalBranchDemandSplit();
ScenarioW_ProportionalLocalAndDownstreamDemand();
ScenarioX_ProportionalCapacityLimitedBranchRedistributes();
ScenarioY_DefaultAllocationModeRemainsGreedy();
ScenarioZ_ProportionalAllocationIsDeterministic();
ScenarioAA_AllocationModeSerializes();
ScenarioAB_DefaultAllocationModeSerializesAndBackfillsTraffic();
ScenarioAC_DefaultAllocationModeAppliesToAllTrafficDefinitions();
ScenarioAD_MissingRouteChoiceFieldsBackfillFromLegacyAllocationMode();
ScenarioAE_MixedRouteChoiceModelsCoexist();
ScenarioAF_PriorityImprovesScarceCapacityAccess();
ScenarioAG_SeededStochasticIsRepeatableAndSeedSensitive();
ScenarioAH_CongestionCanShiftRouteChoice();
ScenarioAI_TemporalMixedRoutingLaunches();
ScenarioAJ_FlagshipSampleLoadsAndExercisesMixedRouting();
ScenarioAK_CanvasLayersDefaultCombinedAndVisible();
ScenarioAL_LayerTogglesDriveVisibleTraffic();
ScenarioAM_InspectorAndReportsOpenOnDemand();
ScenarioAN_ReportRouteSelectionHighlightsCanvas();
ScenarioAO_TimelineAndCanvasOnlyUpdateSurfaces();
ScenarioAP_InspectorTabsAndManualCloseBehave();
ScenarioAQ_EdgeToolTipAndReportEmptyStatesArePopulated();

Console.WriteLine("Verification passed.");

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
    var (network, state) = CreateBakeryNetwork(wheat: 1d, water: 0d);
    var engine = new TemporalNetworkSimulationEngine();
    engine.Advance(network, state);

    AssertEqual(0d, GetAvailableSupply(state, "Bakery", "Bread"), "H bread output");
    AssertEqual(1d, GetAvailableSupply(state, "Bakery", "Wheat"), "H wheat remains");
    AssertEqual(0d, GetAvailableSupply(state, "Bakery", "Water"), "H water remains");
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

static void ScenarioK_FractionalPrecursorProduction()
{
    var (network, state) = CreateBakeryNetwork(wheat: 0.5d, water: 1d);
    var engine = new TemporalNetworkSimulationEngine();
    engine.Advance(network, state);

    AssertEqual(0.5d, GetAvailableSupply(state, "Bakery", "Bread"), "K fractional bread output");
    AssertEqual(0d, GetAvailableSupply(state, "Bakery", "Wheat"), "K fractional wheat deducted");
    AssertEqual(0d, GetAvailableSupply(state, "Bakery", "Water"), "K fractional water deducted");
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

static void ScenarioN_DotnetRunProjectArgumentDoesNotEnterCli()
{
    var service = new CommandLineRunService();
    if (service.ShouldRunFromCommandLine(["MedWNetworkSim.App.csproj"]))
    {
        throw new InvalidOperationException("N project path argument should not enter command-line mode.");
    }

    if (!service.ShouldRunFromCommandLine(["run", "--file", "demo.json", "--output", "report.html"]))
    {
        throw new InvalidOperationException("N explicit run command should enter command-line mode.");
    }

    if (!service.ShouldRunFromCommandLine(["--file", "demo.json", "--output", "report.html"]))
    {
        throw new InvalidOperationException("N named legacy run arguments should enter command-line mode.");
    }
}

static void ScenarioO_WaitingOnBlockedNextEdge()
{
    var network = CreateThreeNodeNetwork(abTime: 1d, bcTime: 3d, bcCapacity: 100d);
    var engine = new TemporalNetworkSimulationEngine();
    var state = CreateBlockedTransitionState(network, blockingRemainingPeriods: 3);

    var result = engine.Advance(network, state);

    AssertEqual(0d, state.OccupiedEdgeCapacity.GetValueOrDefault("AB"), "O released completed edge");
    AssertEqual(100d, state.OccupiedEdgeCapacity.GetValueOrDefault("BC"), "O blocked edge remains within capacity");
    AssertEqual(1d, state.InFlightMovements.Count(movement => movement.IsWaitingBetweenEdges), "O waiting movement count");
    AssertResultAtMostConfiguredCapacity(network, result, "O period snapshot");
    AssertStateAtMostConfiguredCapacity(network, state, "O durable state");
}

static void ScenarioP_RetryAfterCapacityFrees()
{
    var network = CreateThreeNodeNetwork(abTime: 1d, bcTime: 2d, bcCapacity: 100d);
    var engine = new TemporalNetworkSimulationEngine();
    var state = CreateBlockedTransitionState(network, blockingRemainingPeriods: 1);

    engine.Advance(network, state);
    AssertEqual(1d, state.InFlightMovements.Count(movement => movement.IsWaitingBetweenEdges), "P waiting before retry");

    engine.Advance(network, state);

    AssertEqual(0d, state.InFlightMovements.Count(movement => movement.IsWaitingBetweenEdges), "P waiting after retry");
    AssertEqual(100d, state.OccupiedEdgeCapacity.GetValueOrDefault("BC"), "P retried movement occupies next edge");
    AssertStateAtMostConfiguredCapacity(network, state, "P durable state");
}

static void ScenarioQ_BlockedByTranshipmentCapacity()
{
    var network = CreateFourNodeNetwork(abTime: 1d, bcTime: 3d, cdTime: 3d, transhipmentCapacityAtC: 100d);
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);

    state.InFlightMovements.Add(new TemporalNetworkSimulationEngine.TemporalInFlightMovement
    {
        TrafficType = "med",
        Quantity = 100d,
        PathNodeIds = ["A", "B", "C", "D"],
        PathNodeNames = ["A", "B", "C", "D"],
        PathEdgeIds = ["AB", "BC", "CD"],
        CurrentEdgeIndex = 0,
        RemainingPeriodsOnCurrentEdge = 1
    });
    state.InFlightMovements.Add(new TemporalNetworkSimulationEngine.TemporalInFlightMovement
    {
        TrafficType = "med",
        Quantity = 100d,
        PathNodeIds = ["A", "B", "C", "D"],
        PathNodeNames = ["A", "B", "C", "D"],
        PathEdgeIds = ["AB", "BC", "CD"],
        CurrentEdgeIndex = 1,
        RemainingPeriodsOnCurrentEdge = 3
    });
    state.OccupiedEdgeCapacity["AB"] = 100d;
    state.OccupiedEdgeCapacity["BC"] = 100d;
    state.OccupiedTranshipmentCapacity["B"] = 100d;
    state.OccupiedTranshipmentCapacity["C"] = 100d;

    var result = engine.Advance(network, state);

    AssertEqual(1d, state.InFlightMovements.Count(movement => movement.IsWaitingBetweenEdges), "Q waiting movement count");
    AssertEqual(0d, state.OccupiedEdgeCapacity.GetValueOrDefault("AB"), "Q released completed edge");
    AssertEqual(100d, state.OccupiedTranshipmentCapacity.GetValueOrDefault("C"), "Q blocked transhipment remains within capacity");
    AssertResultAtMostConfiguredCapacity(network, result, "Q period snapshot");
    AssertStateAtMostConfiguredCapacity(network, state, "Q durable state");
}

static void ScenarioR_NoOrphanedOrDoubleCountedWaitingOccupancy()
{
    var network = CreateThreeNodeNetwork(abTime: 1d, bcTime: 3d, bcCapacity: 100d);
    var engine = new TemporalNetworkSimulationEngine();
    var state = CreateBlockedTransitionState(network, blockingRemainingPeriods: 3);

    engine.Advance(network, state);
    engine.Advance(network, state);

    AssertEqual(1d, state.InFlightMovements.Count(movement => movement.IsWaitingBetweenEdges), "R waiting movement count");
    AssertEqual(2d, state.InFlightMovements.Count, "R in-flight movement count");
    AssertEqual(0d, state.OccupiedEdgeCapacity.GetValueOrDefault("AB"), "R completed edge not retained");
    AssertEqual(100d, state.OccupiedEdgeCapacity.GetValueOrDefault("BC"), "R blocked edge not double-counted");
    AssertStateAtMostConfiguredCapacity(network, state, "R durable state");
}

static void ScenarioS_StaticRecipeProcurementWithoutInputConsumerProfile()
{
    var network = CreateRecipeProcurementNetwork(edgeTime: 1d, wheatProduction: 10d, breadProduction: 10d);
    var outcomes = new NetworkSimulationEngine().Simulate(network);
    var wheatOutcome = outcomes.Single(outcome => outcome.TrafficType == "Wheat");
    var wheatAllocation = wheatOutcome.Allocations.Single(allocation => allocation.ProducerNodeId == "Farm" && allocation.ConsumerNodeId == "Bakery");

    AssertEqual(10d, wheatAllocation.Quantity, "S static recipe wheat allocation");
    AssertEqual(10d, wheatOutcome.TotalConsumption, "S static implicit wheat demand");
}

static void ScenarioT_TemporalRecipeProcurementWithoutInputConsumerProfile()
{
    var network = CreateRecipeProcurementNetwork(edgeTime: 1d, wheatProduction: 10d, breadProduction: 10d);
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);

    var period1 = engine.Advance(network, state);
    var period2 = engine.Advance(network, state);

    AssertEqual(10d, period1.Allocations.Where(allocation => allocation.TrafficType == "Wheat").Sum(allocation => allocation.Quantity), "T temporal wheat procurement");
    AssertEqual(10d, GetAvailableSupply(state, "Bakery", "Bread"), "T temporal bread output after input arrives");
    AssertEqual(0d, GetAvailableSupply(state, "Bakery", "Wheat"), "T temporal wheat consumed by recipe");
    AssertEqual(0d, period2.Allocations.Where(allocation => allocation.TrafficType == "Wheat").Sum(allocation => allocation.Quantity), "T temporal no duplicate wheat procurement");
}

static void ScenarioU_TemporalRecipeProcurementUsesLocalStockFirst()
{
    var network = CreateRecipeProcurementNetwork(edgeTime: 1d, wheatProduction: 10d, breadProduction: 10d);
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);
    state.GetOrCreateNodeTrafficState("Bakery", "Wheat").AvailableSupply = 4d;

    var period1 = engine.Advance(network, state);

    AssertEqual(6d, period1.Allocations.Where(allocation => allocation.TrafficType == "Wheat").Sum(allocation => allocation.Quantity), "U temporal unmet wheat procurement");
    AssertEqual(4d, GetAvailableSupply(state, "Bakery", "Bread"), "U temporal local-stock bread output");
    AssertEqual(6d, GetAvailableSupply(state, "Bakery", "Wheat"), "U temporal routed wheat retained for later production");
}

static void ScenarioUV_TemporalRecipeOutputInheritsLandedInputCost()
{
    var network = CreateRecipeProcurementNetwork(edgeTime: 1d, wheatProduction: 10d, breadProduction: 10d);
    network.Edges[0].Cost = 2d;
    network.Nodes.Add(new NodeModel
    {
        Id = "Bakery2",
        Name = "Bakery 2",
        TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Bread", Consumption = 5d, ConsumptionStartPeriod = 2 }]
    });
    network.Edges.Add(new EdgeModel { Id = "BakeryBakery2", FromNodeId = "Bakery", ToNodeId = "Bakery2", Time = 1d, Cost = 5d, Capacity = 100d, IsBidirectional = false });

    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);

    var period1 = engine.Advance(network, state);
    var period2 = engine.Advance(network, state);

    var wheatDelivery = period1.Allocations.Single(allocation => allocation.TrafficType == "Wheat" && allocation.ConsumerNodeId == "Bakery");
    var breadDelivery = period2.Allocations.Single(allocation => allocation.TrafficType == "Bread" && allocation.ConsumerNodeId == "Bakery2");

    AssertEqual(2d, wheatDelivery.DeliveredCostPerUnit, "UV wheat landed unit cost");
    AssertEqual(2d, GetAvailableSupplyUnitCost(state, "Bakery", "Bread"), "UV bakery bread source unit cost");
    AssertEqual(2d, breadDelivery.SourceUnitCostPerUnit, "UV bread source unit cost");
    AssertEqual(7d, breadDelivery.DeliveredCostPerUnit, "UV downstream bread delivered unit cost");
}

static void ScenarioUA_TemporalStoreConsumersReplenishAfterRecurringConsumption()
{
    var network = CreateStoreReplenishmentNetwork(storeCapacity: 10d, recurringConsumption: 10d, recurringProduction: 10d);
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);
    var delivered = 0d;
    var activeDeliveryPeriods = 0;

    for (var period = 0; period < 6; period++)
    {
        var result = engine.Advance(network, state);
        var periodDelivered = result.Allocations
            .Where(allocation => allocation.TrafficType == "Grain" && allocation.ConsumerNodeId == "Store")
            .Sum(allocation => allocation.Quantity);
        delivered += periodDelivered;
        if (periodDelivered > 0.001d)
        {
            activeDeliveryPeriods++;
        }

        AssertAtMost(10d, GetStoreInventory(state, "Store", "Grain"), $"UA period {period + 1} store inventory");
    }

    if (activeDeliveryPeriods < 5)
    {
        throw new InvalidOperationException($"UA store demand stopped recurring; only {activeDeliveryPeriods} period(s) delivered.");
    }

    if (delivered <= 10d)
    {
        throw new InvalidOperationException($"UA expected replenishment to exceed one-time store capacity, delivered {delivered}.");
    }
}

static void ScenarioUB_NonStoreTemporalDemandBacklogStillAccumulates()
{
    var network = CreateUnservedNonStoreDemandNetwork(consumption: 7d);
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);

    engine.Advance(network, state);
    engine.Advance(network, state);
    engine.Advance(network, state);

    AssertEqual(21d, GetDemandBacklog(state, "Consumer", "Grain"), "UB non-store demand backlog");
}

static void ScenarioUC_RecipeInputStoresStillFeedProduction()
{
    var network = CreateRecipeInputStoreNetwork();
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);
    state.GetOrCreateNodeTrafficState("Bakery", "Wheat").StoreInventory = 10d;

    engine.Advance(network, state);

    AssertEqual(10d, GetAvailableSupply(state, "Bakery", "Bread"), "UC bread production from store input");
    AssertEqual(0d, GetStoreInventory(state, "Bakery", "Wheat"), "UC wheat store inventory consumed by recipe");
}

static void ScenarioV_ProportionalBranchDemandSplit()
{
    var network = CreateBranchDemandNetwork(allocationMode: AllocationMode.ProportionalBranchDemand);
    var wheatOutcome = new NetworkSimulationEngine().Simulate(network).Single(outcome => outcome.TrafficType == "Wheat");

    AssertEqual(10d, SumEdgeQuantity(wheatOutcome, "BD"), "V B to D branch");
    AssertEqual(30d, SumEdgeQuantity(wheatOutcome, "BC"), "V B to C branch");
    AssertEqual(10d, QuantityTo(wheatOutcome, "C"), "V C local demand");
    AssertEqual(20d, QuantityTo(wheatOutcome, "E"), "V E downstream demand");
}

static void ScenarioW_ProportionalLocalAndDownstreamDemand()
{
    var network = CreateBranchDemandNetwork(allocationMode: AllocationMode.ProportionalBranchDemand);
    var wheatOutcome = new NetworkSimulationEngine().Simulate(network).Single(outcome => outcome.TrafficType == "Wheat");

    AssertEqual(30d, SumEdgeQuantity(wheatOutcome, "BC"), "W C branch includes local plus downstream demand");
    AssertEqual(20d, SumEdgeQuantity(wheatOutcome, "CE"), "W C forwards only remainder after local demand");
}

static void ScenarioX_ProportionalCapacityLimitedBranchRedistributes()
{
    var network = CreateBranchDemandNetwork(
        allocationMode: AllocationMode.ProportionalBranchDemand,
        supply: 40d,
        cDemand: 30d,
        dDemand: 50d,
        eDemand: 0d,
        bcCapacity: 10d);
    var wheatOutcome = new NetworkSimulationEngine().Simulate(network).Single(outcome => outcome.TrafficType == "Wheat");

    AssertEqual(10d, SumEdgeQuantity(wheatOutcome, "BC"), "X capacity-limited C branch");
    AssertEqual(30d, SumEdgeQuantity(wheatOutcome, "BD"), "X redistributed D branch");
}

static void ScenarioY_DefaultAllocationModeRemainsGreedy()
{
    var network = CreateBranchDemandNetwork(supply: 40d, cDemand: 30d, dDemand: 30d, eDemand: 0d);
    if (network.TrafficTypes[0].AllocationMode != AllocationMode.GreedyBestRoute)
    {
        throw new InvalidOperationException("Y new traffic definitions should default to greedy allocation.");
    }

    var wheatOutcome = new NetworkSimulationEngine().Simulate(network).Single(outcome => outcome.TrafficType == "Wheat");

    AssertEqual(30d, SumEdgeQuantity(wheatOutcome, "BC"), "Y default greedy chooses first best route before splitting");
    AssertEqual(10d, SumEdgeQuantity(wheatOutcome, "BD"), "Y default greedy remainder");
}

static void ScenarioZ_ProportionalAllocationIsDeterministic()
{
    var network = CreateBranchDemandNetwork(allocationMode: AllocationMode.ProportionalBranchDemand);
    var signatures = Enumerable.Range(0, 3)
        .Select(_ =>
        {
            var outcome = new NetworkSimulationEngine().Simulate(network).Single(item => item.TrafficType == "Wheat");
            return string.Join(
                "|",
                outcome.Allocations.Select(allocation =>
                    $"{allocation.ConsumerNodeId}:{allocation.Quantity:0.######}:{string.Join(">", allocation.PathEdgeIds)}"));
        })
        .Distinct()
        .ToList();

    AssertEqual(1d, signatures.Count, "Z deterministic proportional allocation signature count");
}

static void ScenarioAA_AllocationModeSerializes()
{
    var service = new NetworkFileService();
    var network = CreateBranchDemandNetwork(allocationMode: AllocationMode.ProportionalBranchDemand);
    var path = Path.Combine(Path.GetTempPath(), $"medwnetworksim-allocation-mode-{Guid.NewGuid():N}.json");

    try
    {
        service.Save(network, path);
        var loaded = service.Load(path);

        if (loaded.TrafficTypes.Single(item => item.Name == "Wheat").AllocationMode != AllocationMode.ProportionalBranchDemand)
        {
            throw new InvalidOperationException("AA allocation mode did not round-trip through JSON.");
        }
    }
    finally
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

static void ScenarioAB_DefaultAllocationModeSerializesAndBackfillsTraffic()
{
    const string json = """
{
  "name": "Default allocation",
  "defaultAllocationMode": "proportionalBranchDemand",
  "nodes": [
    {
      "id": "A",
      "trafficProfiles": [
        {
          "trafficType": "Wheat",
          "production": 1
        }
      ]
    }
  ],
  "edges": []
}
""";
    var network = new NetworkFileService().LoadJson(json);

    if (network.DefaultAllocationMode != AllocationMode.ProportionalBranchDemand)
    {
        throw new InvalidOperationException("AB default allocation mode did not load from JSON.");
    }

    if (network.TrafficTypes.Single(item => item.Name == "Wheat").AllocationMode != AllocationMode.ProportionalBranchDemand)
    {
        throw new InvalidOperationException("AB back-filled traffic type did not use the network default allocation mode.");
    }
}

static void ScenarioAC_DefaultAllocationModeAppliesToAllTrafficDefinitions()
{
    var viewModel = new MainWindowViewModel
    {
        DefaultAllocationMode = AllocationMode.ProportionalBranchDemand
    };

    foreach (var definition in viewModel.TrafficDefinitions)
    {
        definition.AllocationMode = AllocationMode.GreedyBestRoute;
    }

    viewModel.ApplyDefaultAllocationModeToAllTrafficDefinitions();

    if (viewModel.TrafficDefinitions.Any(definition => definition.AllocationMode != AllocationMode.ProportionalBranchDemand))
    {
        throw new InvalidOperationException("AC default allocation mode was not applied to all traffic definitions.");
    }
}

static void ScenarioAD_MissingRouteChoiceFieldsBackfillFromLegacyAllocationMode()
{
    const string json = """
{
  "name": "Legacy route choice",
  "trafficTypes": [
    { "name": "Fast", "allocationMode": "greedyBestRoute" },
    { "name": "Bulk", "allocationMode": "proportionalBranchDemand" }
  ],
  "nodes": [
    { "id": "A", "trafficProfiles": [{ "trafficType": "Fast", "production": 1 }, { "trafficType": "Bulk", "production": 1 }] },
    { "id": "B", "trafficProfiles": [{ "trafficType": "Fast", "consumption": 1 }, { "trafficType": "Bulk", "consumption": 1 }] }
  ],
  "edges": [{ "id": "AB", "fromNodeId": "A", "toNodeId": "B", "time": 1, "cost": 1, "capacity": 10 }]
}
""";
    var network = new NetworkFileService().LoadJson(json);

    if (network.TrafficTypes.Single(item => item.Name == "Fast").FlowSplitPolicy != FlowSplitPolicy.SinglePath)
    {
        throw new InvalidOperationException("AD greedy legacy allocation did not map to single path.");
    }

    if (network.TrafficTypes.Single(item => item.Name == "Bulk").FlowSplitPolicy != FlowSplitPolicy.MultiPath)
    {
        throw new InvalidOperationException("AD proportional legacy allocation did not map to multi path.");
    }
}

static void ScenarioAE_MixedRouteChoiceModelsCoexist()
{
    var network = CreateMixedRoutingNetwork();
    var outcomes = new NetworkSimulationEngine().Simulate(network);

    AssertEqual(2d, outcomes.Count, "AE mixed outcome count");
    AssertEqual(2d, outcomes.Count(outcome => outcome.Allocations.Count > 0), "AE mixed allocation count");
    if (outcomes.All(outcome => outcome.Notes.All(note => !note.Contains("route choice", StringComparison.OrdinalIgnoreCase))))
    {
        throw new InvalidOperationException("AE mixed route-choice notes were not emitted.");
    }
}

static void ScenarioAF_PriorityImprovesScarceCapacityAccess()
{
    var network = CreateMixedRoutingNetwork();
    var outcomes = new NetworkSimulationEngine().Simulate(network);

    var official = outcomes.Single(outcome => outcome.TrafficType == "Official").TotalDelivered;
    if (official < 10d)
    {
        throw new InvalidOperationException($"AF expected higher priority official flow to retain full scarce-route access, official {official}.");
    }
}

static void ScenarioAG_SeededStochasticIsRepeatableAndSeedSensitive()
{
    var first = GetStochasticSignature(seed: 10);
    var second = GetStochasticSignature(seed: 10);
    var third = GetStochasticSignature(seed: 99);

    if (first != second)
    {
        throw new InvalidOperationException("AG same seed did not produce the same stochastic signature.");
    }

    if (first == third)
    {
        throw new InvalidOperationException("AG different seed did not change the stochastic signature.");
    }
}

static void ScenarioAH_CongestionCanShiftRouteChoice()
{
    var network = CreateCongestionRouteNetwork(seed: 2);
    var outcome = new NetworkSimulationEngine().Simulate(network).Single(outcome => outcome.TrafficType == "Civilian");
    var usedRoutes = outcome.Allocations.Select(allocation => string.Join(">", allocation.PathEdgeIds)).Distinct().Count();

    if (usedRoutes < 2)
    {
        throw new InvalidOperationException("AH expected congestion-sensitive multi-path routing to use alternate routes.");
    }
}

static void ScenarioAI_TemporalMixedRoutingLaunches()
{
    var network = CreateCongestionRouteNetwork(seed: 2);
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);
    var result = engine.Advance(network, state);

    if (!result.Allocations.Any(allocation => allocation.TrafficType == "Civilian"))
    {
        throw new InvalidOperationException("AI temporal mixed routing did not launch civilian traffic.");
    }
}

static void ScenarioAJ_FlagshipSampleLoadsAndExercisesMixedRouting()
{
    var samplePath = Path.Combine(AppContext.BaseDirectory, "Samples", "sample-network.json");
    var network = new NetworkFileService().Load(samplePath);

    if (network.Nodes.Count < 20)
    {
        throw new InvalidOperationException($"AJ flagship sample expected at least 20 nodes, found {network.Nodes.Count}.");
    }

    if (network.TrafficTypes.Count < 5)
    {
        throw new InvalidOperationException($"AJ flagship sample expected at least 5 traffic types, found {network.TrafficTypes.Count}.");
    }

    if (!network.TrafficTypes.Any(item => item.RouteChoiceModel == RouteChoiceModel.SystemOptimal) ||
        !network.TrafficTypes.Any(item => item.RouteChoiceModel == RouteChoiceModel.StochasticUserResponsive))
    {
        throw new InvalidOperationException("AJ flagship sample does not exercise both route-choice models.");
    }

    if (!network.TrafficTypes.Any(item => item.FlowSplitPolicy == FlowSplitPolicy.SinglePath) ||
        !network.TrafficTypes.Any(item => item.FlowSplitPolicy == FlowSplitPolicy.MultiPath))
    {
        throw new InvalidOperationException("AJ flagship sample does not exercise both flow split policies.");
    }

    var outcomes = new NetworkSimulationEngine().Simulate(network);
    if (outcomes.Count == 0 || outcomes.All(outcome => outcome.Allocations.Count == 0))
    {
        throw new InvalidOperationException("AJ flagship sample loaded but did not simulate any allocations.");
    }
}

static void ScenarioAK_CanvasLayersDefaultCombinedAndVisible()
{
    var viewModel = new MainWindowViewModel();

    if (viewModel.LayersPanel.SelectedDisplayMode != CanvasDisplayMode.Combined)
    {
        throw new InvalidOperationException("AK default canvas display mode should be Combined.");
    }

    if (!viewModel.LayersPanel.ShowCombinedTraffic || viewModel.LayersPanel.TrafficLayers.Any(layer => !layer.IsVisible))
    {
        throw new InvalidOperationException("AK all traffic should be visible by default.");
    }
}

static void ScenarioAL_LayerTogglesDriveVisibleTraffic()
{
    var viewModel = CreateSampleViewModel();
    viewModel.LayersPanel.SelectedDisplayMode = CanvasDisplayMode.SelectedOnly;
    foreach (var layer in viewModel.LayersPanel.TrafficLayers)
    {
        layer.IsVisible = false;
    }

    var grain = viewModel.LayersPanel.TrafficLayers.First(layer => layer.TrafficType == "Grain");
    grain.IsVisible = true;
    grain.IsHighlighted = true;

    if (!viewModel.LayersPanel.ShouldIncludeTraffic("Grain") || viewModel.LayersPanel.ShouldIncludeTraffic("Bread"))
    {
        throw new InvalidOperationException("AL layer visible state did not drive selected-only inclusion.");
    }

    if (!viewModel.LayersPanel.HighlightedTrafficTypes.Contains("Grain"))
    {
        throw new InvalidOperationException("AL highlighted traffic state did not update.");
    }
}

static void ScenarioAM_InspectorAndReportsOpenOnDemand()
{
    var viewModel = CreateSampleViewModel();

    if (viewModel.InspectorPanel.IsOpen || viewModel.ReportsDrawer.IsOpen)
    {
        throw new InvalidOperationException("AM inspector and reports should be hidden by default.");
    }

    viewModel.SelectedNode = viewModel.Nodes.First();
    viewModel.ToggleReportsDrawer();

    if (!viewModel.InspectorPanel.IsOpen || !viewModel.ReportsDrawer.IsOpen)
    {
        throw new InvalidOperationException("AM inspector or reports did not open on demand.");
    }

    viewModel.ToggleLayersPanel();
    viewModel.ToggleLegendPanel();
    if (!viewModel.IsLayersPanelOpen || !viewModel.IsLegendPanelOpen)
    {
        throw new InvalidOperationException("AM layers or legend panel did not open.");
    }

    viewModel.ToggleLayersPanel();
    viewModel.ToggleInspectorPanel();
    viewModel.ToggleReportsDrawer();
    viewModel.ToggleLegendPanel();
    if (viewModel.IsLayersPanelOpen || viewModel.InspectorPanel.IsOpen || viewModel.ReportsDrawer.IsOpen || viewModel.IsLegendPanelOpen)
    {
        throw new InvalidOperationException("AM optional panels did not close cleanly.");
    }
}

static void ScenarioAN_ReportRouteSelectionHighlightsCanvas()
{
    var viewModel = CreateSampleViewModel();
    viewModel.RunSimulation();
    viewModel.ToggleReportsDrawer();

    var route = viewModel.VisibleAllocations.First(allocation => allocation.PathEdgeIds.Count > 0);
    viewModel.ReportsDrawer.SelectedReportRow = route;

    if (viewModel.Canvas.HighlightedRouteTrafficType != route.TrafficType ||
        string.IsNullOrWhiteSpace(viewModel.Canvas.HighlightedRoutePath) ||
        !viewModel.InspectorPanel.IsOpen ||
        !viewModel.Canvas.HighlightedRouteEdgeIds.SequenceEqual(route.PathEdgeIds))
    {
        throw new InvalidOperationException("AN route report selection did not sync to canvas highlight and inspector.");
    }
}

static void ScenarioAO_TimelineAndCanvasOnlyUpdateSurfaces()
{
    var viewModel = new MainWindowViewModel();
    viewModel.ToggleLayersPanel();
    viewModel.ToggleReportsDrawer();
    viewModel.AdvanceTimeline();

    if (viewModel.Canvas.CurrentPeriod != viewModel.CurrentPeriod || viewModel.TimelineToolbar.CurrentPeriod != viewModel.CurrentPeriod)
    {
        throw new InvalidOperationException("AO timeline context did not update visible surface state.");
    }

    viewModel.ToggleCanvasOnlyMode();
    if (viewModel.OptionalSidePanelVisibility != System.Windows.Visibility.Collapsed ||
        viewModel.BottomWorkspaceVisibility != System.Windows.Visibility.Collapsed)
    {
        throw new InvalidOperationException("AO Canvas Only mode did not hide optional side/drawer surfaces.");
    }

    viewModel.ToggleCanvasOnlyMode();
    if (viewModel.OptionalSidePanelVisibility != System.Windows.Visibility.Visible ||
        viewModel.BottomWorkspaceVisibility != System.Windows.Visibility.Visible)
    {
        throw new InvalidOperationException("AO exiting Canvas Only mode did not restore previous optional surface states.");
    }
}

static void ScenarioAP_InspectorTabsAndManualCloseBehave()
{
    var viewModel = CreateSampleViewModel();
    viewModel.SelectedNode = viewModel.Nodes.First();

    if (!viewModel.InspectorPanel.IsOpen ||
        viewModel.InspectorPanel.SummaryText == viewModel.InspectorPanel.FlowsText ||
        viewModel.InspectorPanel.SummaryText == viewModel.InspectorPanel.CapacityText)
    {
        throw new InvalidOperationException("AP inspector tabs should expose distinct node content.");
    }

    viewModel.InspectorPanel.SelectedTab = InspectorTab.Flows;
    if (viewModel.InspectorPanel.SelectedTabIndex != (int)InspectorTab.Flows)
    {
        throw new InvalidOperationException("AP inspector tab selection did not track the selected enum value.");
    }

    viewModel.ToggleInspectorPanel();
    viewModel.SelectedEdge = viewModel.Edges.First();
    if (viewModel.InspectorPanel.IsOpen)
    {
        throw new InvalidOperationException("AP manually closed inspector should stay closed during contextual selection.");
    }

    viewModel.ToggleInspectorPanel();
    if (!viewModel.InspectorPanel.IsOpen)
    {
        throw new InvalidOperationException("AP inspector did not reopen explicitly after manual close.");
    }
}

static void ScenarioAQ_EdgeToolTipAndReportEmptyStatesArePopulated()
{
    var viewModel = CreateSampleViewModel();
    var edge = viewModel.Edges.First();
    if (!edge.EdgeToolTipText.Contains(edge.Id, StringComparison.Ordinal) ||
        !edge.EdgeToolTipText.Contains(edge.FromNodeId, StringComparison.Ordinal) ||
        !edge.EdgeToolTipText.Contains(edge.ToNodeId, StringComparison.Ordinal) ||
        !edge.EdgeToolTipText.Contains("Utilization:", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("AQ edge tooltip text is missing expected routing/capacity details.");
    }

    viewModel.ToggleReportsDrawer();
    if (viewModel.BottomWorkspaceVisibility != System.Windows.Visibility.Visible ||
        viewModel.RoutesEmptyStateVisibility != System.Windows.Visibility.Visible ||
        !viewModel.RoutesEmptyText.Contains("No results yet", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("AQ reports drawer did not expose the no-results empty state.");
    }

    viewModel.RunSimulation();
    if (viewModel.RoutesGridVisibility != System.Windows.Visibility.Visible ||
        !viewModel.RoutesTabHeader.Contains(viewModel.VisibleAllocations.Count.ToString(), StringComparison.Ordinal))
    {
        throw new InvalidOperationException("AQ reports drawer did not expose populated rows and counts after simulation.");
    }
}

static MainWindowViewModel CreateSampleViewModel()
{
    var viewModel = new MainWindowViewModel();
    viewModel.LoadBundledSample();
    return viewModel;
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

static NetworkModel CreateBranchDemandNetwork(
    AllocationMode allocationMode = AllocationMode.GreedyBestRoute,
    double supply = 40d,
    double cDemand = 10d,
    double dDemand = 10d,
    double eDemand = 20d,
    double bcCapacity = 100d)
{
    return new NetworkModel
    {
        Name = "Branch demand split",
        TrafficTypes =
        [
            new TrafficTypeDefinition
            {
                Name = "Wheat",
                RoutingPreference = RoutingPreference.TotalCost,
                AllocationMode = allocationMode
            }
        ],
        Nodes =
        [
            new NodeModel
            {
                Id = "A",
                Name = "A",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Wheat", Production = supply }]
            },
            new NodeModel
            {
                Id = "B",
                Name = "B",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Wheat", CanTransship = true }]
            },
            new NodeModel
            {
                Id = "C",
                Name = "C",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Wheat", Consumption = cDemand, CanTransship = true }]
            },
            new NodeModel
            {
                Id = "D",
                Name = "D",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Wheat", Consumption = dDemand }]
            },
            new NodeModel
            {
                Id = "E",
                Name = "E",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Wheat", Consumption = eDemand }]
            }
        ],
        Edges =
        [
            new EdgeModel { Id = "AB", FromNodeId = "A", ToNodeId = "B", Time = 1d, Cost = 1d, Capacity = 100d, IsBidirectional = false },
            new EdgeModel { Id = "BC", FromNodeId = "B", ToNodeId = "C", Time = 1d, Cost = 1d, Capacity = bcCapacity, IsBidirectional = false },
            new EdgeModel { Id = "BD", FromNodeId = "B", ToNodeId = "D", Time = 1d, Cost = 2d, Capacity = 100d, IsBidirectional = false },
            new EdgeModel { Id = "CE", FromNodeId = "C", ToNodeId = "E", Time = 1d, Cost = 1d, Capacity = 100d, IsBidirectional = false }
        ]
    };
}

static NetworkModel CreateMixedRoutingNetwork()
{
    var network = CreateCongestionRouteNetwork(seed: 1, capacity: 10d, civilianSupply: 10d, officialSupply: 10d);
    network.TrafficTypes =
    [
        new TrafficTypeDefinition
        {
            Name = "Official",
            RoutingPreference = RoutingPreference.Speed,
            RouteChoiceModel = RouteChoiceModel.SystemOptimal,
            FlowSplitPolicy = FlowSplitPolicy.SinglePath,
            RouteChoiceSettings = new RouteChoiceSettings { Priority = 5d, MaxCandidateRoutes = 2, IterationCount = 2 },
            CapacityBidPerUnit = 2d
        },
        new TrafficTypeDefinition
        {
            Name = "Civilian",
            RoutingPreference = RoutingPreference.TotalCost,
            RouteChoiceModel = RouteChoiceModel.StochasticUserResponsive,
            FlowSplitPolicy = FlowSplitPolicy.MultiPath,
            RouteChoiceSettings = new RouteChoiceSettings { Priority = 0.5d, MaxCandidateRoutes = 2, IterationCount = 2, RouteDiversity = 0.4d },
            CapacityBidPerUnit = 0.1d
        }
    ];
    return network;
}

static NetworkModel CreateCongestionRouteNetwork(
    int seed,
    double capacity = 100d,
    double civilianSupply = 40d,
    double officialSupply = 0d)
{
    return new NetworkModel
    {
        Name = "Congestion route",
        SimulationSeed = seed,
        TrafficTypes =
        [
            new TrafficTypeDefinition
            {
                Name = "Civilian",
                RoutingPreference = RoutingPreference.TotalCost,
                RouteChoiceModel = RouteChoiceModel.StochasticUserResponsive,
                FlowSplitPolicy = FlowSplitPolicy.MultiPath,
                RouteChoiceSettings = new RouteChoiceSettings
                {
                    MaxCandidateRoutes = 3,
                    Priority = 1d,
                    InformationAccuracy = 0.75d,
                    RouteDiversity = 0.8d,
                    CongestionSensitivity = 3d,
                    IterationCount = 4
                }
            },
            new TrafficTypeDefinition
            {
                Name = "Official",
                RoutingPreference = RoutingPreference.Speed,
                RouteChoiceModel = RouteChoiceModel.SystemOptimal,
                FlowSplitPolicy = FlowSplitPolicy.SinglePath,
                RouteChoiceSettings = new RouteChoiceSettings { Priority = 4d, MaxCandidateRoutes = 2, IterationCount = 2 }
            }
        ],
        Nodes =
        [
            new NodeModel
            {
                Id = "A",
                Name = "A",
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "Civilian", Production = civilianSupply },
                    new NodeTrafficProfile { TrafficType = "Official", Production = officialSupply }
                ]
            },
            new NodeModel
            {
                Id = "B",
                Name = "B",
                TranshipmentCapacity = capacity,
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "Civilian", CanTransship = true },
                    new NodeTrafficProfile { TrafficType = "Official", CanTransship = true }
                ]
            },
            new NodeModel
            {
                Id = "C",
                Name = "C",
                TranshipmentCapacity = capacity,
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "Civilian", CanTransship = true },
                    new NodeTrafficProfile { TrafficType = "Official", CanTransship = true }
                ]
            },
            new NodeModel
            {
                Id = "D",
                Name = "D",
                TrafficProfiles =
                [
                    new NodeTrafficProfile { TrafficType = "Civilian", Consumption = civilianSupply },
                    new NodeTrafficProfile { TrafficType = "Official", Consumption = officialSupply }
                ]
            }
        ],
        Edges =
        [
            new EdgeModel { Id = "AB", FromNodeId = "A", ToNodeId = "B", Time = 1d, Cost = 1d, Capacity = capacity, IsBidirectional = false },
            new EdgeModel { Id = "BD", FromNodeId = "B", ToNodeId = "D", Time = 1d, Cost = 1d, Capacity = capacity, IsBidirectional = false },
            new EdgeModel { Id = "AC", FromNodeId = "A", ToNodeId = "C", Time = 1.2d, Cost = 1.4d, Capacity = 100d, IsBidirectional = false },
            new EdgeModel { Id = "CD", FromNodeId = "C", ToNodeId = "D", Time = 1.2d, Cost = 1.4d, Capacity = 100d, IsBidirectional = false }
        ]
    };
}

static string GetStochasticSignature(int seed)
{
    var network = CreateCongestionRouteNetwork(seed);
    var outcome = new NetworkSimulationEngine().Simulate(network).Single(outcome => outcome.TrafficType == "Civilian");
    return string.Join("|", outcome.Allocations.Select(allocation => $"{allocation.Quantity:0.###}:{string.Join(">", allocation.PathEdgeIds)}"));
}

static NetworkModel CreateThreeNodeNetwork(double abTime, double bcTime, double bcCapacity)
{
    return new NetworkModel
    {
        Name = "Three node transition",
        TrafficTypes = [new TrafficTypeDefinition { Name = "med", RoutingPreference = RoutingPreference.TotalCost }],
        Nodes =
        [
            new NodeModel
            {
                Id = "A",
                Name = "A",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }]
            },
            new NodeModel
            {
                Id = "B",
                Name = "B",
                TranshipmentCapacity = 100d,
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med", CanTransship = true }]
            },
            new NodeModel
            {
                Id = "C",
                Name = "C",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }]
            }
        ],
        Edges =
        [
            new EdgeModel { Id = "AB", FromNodeId = "A", ToNodeId = "B", Time = abTime, Cost = 1d, Capacity = 100d, IsBidirectional = false },
            new EdgeModel { Id = "BC", FromNodeId = "B", ToNodeId = "C", Time = bcTime, Cost = 1d, Capacity = bcCapacity, IsBidirectional = false }
        ]
    };
}

static NetworkModel CreateFourNodeNetwork(double abTime, double bcTime, double cdTime, double transhipmentCapacityAtC)
{
    return new NetworkModel
    {
        Name = "Four node transition",
        TrafficTypes = [new TrafficTypeDefinition { Name = "med", RoutingPreference = RoutingPreference.TotalCost }],
        Nodes =
        [
            new NodeModel
            {
                Id = "A",
                Name = "A",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }]
            },
            new NodeModel
            {
                Id = "B",
                Name = "B",
                TranshipmentCapacity = 100d,
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med", CanTransship = true }]
            },
            new NodeModel
            {
                Id = "C",
                Name = "C",
                TranshipmentCapacity = transhipmentCapacityAtC,
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med", CanTransship = true }]
            },
            new NodeModel
            {
                Id = "D",
                Name = "D",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "med" }]
            }
        ],
        Edges =
        [
            new EdgeModel { Id = "AB", FromNodeId = "A", ToNodeId = "B", Time = abTime, Cost = 1d, Capacity = 100d, IsBidirectional = false },
            new EdgeModel { Id = "BC", FromNodeId = "B", ToNodeId = "C", Time = bcTime, Cost = 1d, Capacity = 200d, IsBidirectional = false },
            new EdgeModel { Id = "CD", FromNodeId = "C", ToNodeId = "D", Time = cdTime, Cost = 1d, Capacity = 200d, IsBidirectional = false }
        ]
    };
}

static NetworkModel CreateRecipeProcurementNetwork(double edgeTime, double wheatProduction, double breadProduction)
{
    return new NetworkModel
    {
        Name = "Recipe procurement",
        TrafficTypes =
        [
            new TrafficTypeDefinition { Name = "Bread", RoutingPreference = RoutingPreference.TotalCost },
            new TrafficTypeDefinition { Name = "Wheat", RoutingPreference = RoutingPreference.TotalCost }
        ],
        Nodes =
        [
            new NodeModel
            {
                Id = "Farm",
                Name = "Farm",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Wheat", Production = wheatProduction }]
            },
            new NodeModel
            {
                Id = "Bakery",
                Name = "Bakery",
                TrafficProfiles =
                [
                    new NodeTrafficProfile
                    {
                        TrafficType = "Bread",
                        Production = breadProduction,
                        InputRequirements =
                        [
                            new ProductionInputRequirement { TrafficType = "Wheat", QuantityPerOutputUnit = 1d }
                        ]
                    }
                ]
            }
        ],
        Edges =
        [
            new EdgeModel { Id = "FarmBakery", FromNodeId = "Farm", ToNodeId = "Bakery", Time = edgeTime, Cost = 1d, Capacity = 100d, IsBidirectional = false }
        ]
    };
}

static NetworkModel CreateStoreReplenishmentNetwork(double storeCapacity, double recurringConsumption, double recurringProduction)
{
    return new NetworkModel
    {
        Name = "Store replenishment",
        TrafficTypes = [new TrafficTypeDefinition { Name = "Grain", RoutingPreference = RoutingPreference.TotalCost }],
        Nodes =
        [
            new NodeModel
            {
                Id = "Farm",
                Name = "Farm",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Grain", Production = recurringProduction }]
            },
            new NodeModel
            {
                Id = "Store",
                Name = "Store",
                TrafficProfiles =
                [
                    new NodeTrafficProfile
                    {
                        TrafficType = "Grain",
                        Consumption = recurringConsumption,
                        IsStore = true,
                        StoreCapacity = storeCapacity
                    }
                ]
            }
        ],
        Edges =
        [
            new EdgeModel { Id = "FarmStore", FromNodeId = "Farm", ToNodeId = "Store", Time = 1d, Cost = 1d, Capacity = recurringProduction, IsBidirectional = false }
        ]
    };
}

static NetworkModel CreateUnservedNonStoreDemandNetwork(double consumption)
{
    return new NetworkModel
    {
        Name = "Unserved non-store demand",
        TrafficTypes = [new TrafficTypeDefinition { Name = "Grain", RoutingPreference = RoutingPreference.TotalCost }],
        Nodes =
        [
            new NodeModel
            {
                Id = "Consumer",
                Name = "Consumer",
                TrafficProfiles = [new NodeTrafficProfile { TrafficType = "Grain", Consumption = consumption }]
            }
        ]
    };
}

static NetworkModel CreateRecipeInputStoreNetwork()
{
    return new NetworkModel
    {
        Name = "Recipe input store",
        TrafficTypes =
        [
            new TrafficTypeDefinition { Name = "Bread", RoutingPreference = RoutingPreference.TotalCost },
            new TrafficTypeDefinition { Name = "Wheat", RoutingPreference = RoutingPreference.TotalCost }
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
                            new ProductionInputRequirement { TrafficType = "Wheat", QuantityPerOutputUnit = 1d }
                        ]
                    },
                    new NodeTrafficProfile
                    {
                        TrafficType = "Wheat",
                        IsStore = true,
                        StoreCapacity = 20d
                    }
                ]
            }
        ]
    };
}

static TemporalNetworkSimulationEngine.TemporalSimulationState CreateBlockedTransitionState(NetworkModel network, int blockingRemainingPeriods)
{
    var engine = new TemporalNetworkSimulationEngine();
    var state = engine.Initialize(network);
    state.InFlightMovements.Add(new TemporalNetworkSimulationEngine.TemporalInFlightMovement
    {
        TrafficType = "med",
        Quantity = 100d,
        PathNodeIds = ["A", "B", "C"],
        PathNodeNames = ["A", "B", "C"],
        PathEdgeIds = ["AB", "BC"],
        CurrentEdgeIndex = 0,
        RemainingPeriodsOnCurrentEdge = 1
    });
    state.InFlightMovements.Add(new TemporalNetworkSimulationEngine.TemporalInFlightMovement
    {
        TrafficType = "med",
        Quantity = 100d,
        PathNodeIds = ["A", "B", "C"],
        PathNodeNames = ["A", "B", "C"],
        PathEdgeIds = ["AB", "BC"],
        CurrentEdgeIndex = 1,
        RemainingPeriodsOnCurrentEdge = blockingRemainingPeriods
    });
    state.OccupiedEdgeCapacity["AB"] = 100d;
    state.OccupiedEdgeCapacity["BC"] = 100d;
    state.OccupiedTranshipmentCapacity["B"] = 100d;
    return state;
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

static double GetAvailableSupplyUnitCost(
    TemporalNetworkSimulationEngine.TemporalSimulationState state,
    string nodeId,
    string trafficType)
{
    return state.NodeStates.TryGetValue(new TemporalNetworkSimulationEngine.TemporalNodeTrafficKey(nodeId, trafficType), out var nodeState)
        ? nodeState.AvailableSupplyUnitCostPerUnit
        : 0d;
}

static double GetDemandBacklog(
    TemporalNetworkSimulationEngine.TemporalSimulationState state,
    string nodeId,
    string trafficType)
{
    return state.NodeStates.TryGetValue(new TemporalNetworkSimulationEngine.TemporalNodeTrafficKey(nodeId, trafficType), out var nodeState)
        ? nodeState.DemandBacklog
        : 0d;
}

static double GetStoreInventory(
    TemporalNetworkSimulationEngine.TemporalSimulationState state,
    string nodeId,
    string trafficType)
{
    return state.NodeStates.TryGetValue(new TemporalNetworkSimulationEngine.TemporalNodeTrafficKey(nodeId, trafficType), out var nodeState)
        ? nodeState.StoreInventory
        : 0d;
}

static double SumEdgeQuantity(TrafficSimulationOutcome outcome, string edgeId)
{
    return outcome.Allocations
        .Where(allocation => allocation.PathEdgeIds.Contains(edgeId))
        .Sum(allocation => allocation.Quantity);
}

static double QuantityTo(TrafficSimulationOutcome outcome, string nodeId)
{
    return outcome.Allocations
        .Where(allocation => allocation.ConsumerNodeId == nodeId)
        .Sum(allocation => allocation.Quantity);
}

static void AssertEqual(double expected, double actual, string label)
{
    if (Math.Abs(expected - actual) > 0.001d)
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

static void AssertResultAtMostConfiguredCapacity(NetworkModel network, TemporalNetworkSimulationEngine.TemporalSimulationStepResult result, string label)
{
    foreach (var edge in network.Edges.Where(edge => edge.Capacity.HasValue))
    {
        AssertAtMost(edge.Capacity!.Value, result.EdgeOccupancy.GetValueOrDefault(edge.Id), $"{label} edge {edge.Id}");
    }

    foreach (var node in network.Nodes.Where(node => node.TranshipmentCapacity.HasValue))
    {
        AssertAtMost(
            node.TranshipmentCapacity!.Value,
            result.TranshipmentOccupancy.GetValueOrDefault(node.Id),
            $"{label} transhipment {node.Id}");
    }
}

static void AssertStateAtMostConfiguredCapacity(NetworkModel network, TemporalNetworkSimulationEngine.TemporalSimulationState state, string label)
{
    foreach (var edge in network.Edges.Where(edge => edge.Capacity.HasValue))
    {
        AssertAtMost(edge.Capacity!.Value, state.OccupiedEdgeCapacity.GetValueOrDefault(edge.Id), $"{label} edge {edge.Id}");
    }

    foreach (var node in network.Nodes.Where(node => node.TranshipmentCapacity.HasValue))
    {
        AssertAtMost(
            node.TranshipmentCapacity!.Value,
            state.OccupiedTranshipmentCapacity.GetValueOrDefault(node.Id),
            $"{label} transhipment {node.Id}");
    }
}

static void AssertSequenceEqual(IReadOnlyList<int> expected, IReadOnlyList<int> actual, string label)
{
    if (expected.Count != actual.Count || expected.Where((item, index) => item != actual[index]).Any())
    {
        throw new InvalidOperationException($"{label}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}].");
    }
}
