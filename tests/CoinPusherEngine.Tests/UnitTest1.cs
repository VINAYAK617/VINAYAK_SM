using System.Text.Json;

namespace CoinPusherEngine.Tests;

public class UnitTest1
{
    [Fact]
    public void GeneratePlan_VerifiesExactWins_WithoutFeatures()
    {
        var planner = new Planner(featureRegistry: BuildRegistry());
        var input = new MathInput
        {
            Seed = 42,
            TotalBaseSpins = 3,
            TargetCollection = new Dictionary<string, int>
            {
                ["DIAMOND"] = 4,
                ["GOLD"] = 3,
                ["RUBY"] = 2,
            },
        };

        var plan = planner.GeneratePlan(input);
        var execution = GameEngine.Execute(plan);

        Assert.True(plan.Verified);
        Assert.Equal(4, execution.Totals[1]);
        Assert.Equal(3, execution.Totals[2]);
        Assert.Equal(2, execution.Totals[3]);
        Assert.Equal(3, plan.TotalSpins);
    }

    [Fact]
    public void GeneratePlan_SupportsPrizeUpgradeAndFeatureToken()
    {
        var planner = new Planner(featureRegistry: BuildRegistry());
        var input = new MathInput
        {
            Seed = 7,
            TotalBaseSpins = 4,
            TargetCollection = new Dictionary<string, int>
            {
                ["DIAMOND"] = 5,
                ["GOLD"] = 2,
            },
            RequiredFeatures = new Dictionary<string, int>
            {
                [FeatureIds.PrizeUpgrade] = 1,
            },
            PrizeUpgradeMap = new Dictionary<string, string>
            {
                ["SEVEN"] = "DIAMOND",
            },
        };

        var plan = planner.GeneratePlan(input);
        var execution = GameEngine.Execute(plan);
        var hasPrizeUpgradeSpawn = plan.SpinPlans
            .SelectMany(spin => spin.PlannedSpawns.Values)
            .Any(cell => cell.FeatureId == FeatureIds.PrizeUpgrade);

        Assert.True(hasPrizeUpgradeSpawn);
        Assert.Equal(5, execution.Totals[1]);
        Assert.Equal(2, execution.Totals[2]);
    }

    [Fact]
    public void GenerateTicketJson_ProducesCompactTurnStructure()
    {
        var engine = new CoinPusherEngineService(featureRegistry: BuildRegistry());
        var input = new MathInput
        {
            Seed = 99,
            TotalBaseSpins = 2,
            TargetCollection = new Dictionary<string, int>
            {
                ["DIAMOND"] = 2,
                ["GOLD"] = 1,
            },
        };

        var json = engine.GenerateTicketJson(input);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(2, doc.RootElement.GetProperty("winInfo").GetProperty("totalSpins").GetInt32());
        Assert.Equal(5, doc.RootElement.GetProperty("startingBoard").GetArrayLength());
        Assert.Equal(2, doc.RootElement.GetProperty("turns").GetArrayLength());
    }

    [Fact]
    public void GeneratePlan_SupportsWheelAndExtraSpin()
    {
        var planner = new Planner(featureRegistry: BuildRegistry());
        var input = new MathInput
        {
            Seed = 123,
            TotalBaseSpins = 4,
            TargetCollection = new Dictionary<string, int>
            {
                ["GOLD"] = 12,
                ["RUBY"] = 4,
            },
            RequiredFeatures = new Dictionary<string, int>
            {
                [FeatureIds.Wheel] = 1,
                [FeatureIds.ExtraSpin] = 1,
            },
        };

        var plan = planner.GeneratePlan(input);
        var execution = GameEngine.Execute(plan);

        Assert.Equal(5, plan.TotalSpins);
        Assert.Contains(plan.WheelLocks, lockInfo => lockInfo.SymbolId == 2);
        Assert.Contains(
            plan.SpinPlans.SelectMany(spin => spin.PlannedSpawns.Values),
            cell => cell.FeatureId == FeatureIds.Wheel);
        Assert.Equal(12, execution.Totals[2]);
        Assert.Equal(4, execution.Totals[3]);
    }

    [Fact]
    public void ExecuteWithTrace_CapturesSpinStagesAndReadableBoard()
    {
        var planner = new Planner(featureRegistry: BuildRegistry());
        var symbolTable = new SymbolTable();
        var input = new MathInput
        {
            Seed = 321,
            TotalBaseSpins = 3,
            TargetCollection = new Dictionary<string, int>
            {
                ["DIAMOND"] = 4,
                ["GOLD"] = 3,
            },
        };

        var plan = planner.GeneratePlan(input);
        var trace = GameEngine.ExecuteWithTrace(plan);
        var formattedBoard = BoardFormatter.FormatBoard(plan.SpinPlans[0].BoardAtStart, symbolTable);

        Assert.Equal(plan.TotalSpins, trace.SpinTraces.Count);
        Assert.Equal(plan.TotalSpins, trace.Result.BoardHistory.Count);
        Assert.Contains("C0", formattedBoard);
        Assert.Contains("R0", formattedBoard);
    }

    private static IReadOnlyDictionary<string, FeaturePlacementConfig> BuildRegistry()
    {
        return new Dictionary<string, FeaturePlacementConfig>(StringComparer.OrdinalIgnoreCase)
        {
            [FeatureIds.Wheel] = new FeaturePlacementConfig(0.0, 4, 2, 1),
            [FeatureIds.Flush] = new FeaturePlacementConfig(0.0, 5, 1, 2),
            [FeatureIds.ExtraSpin] = new FeaturePlacementConfig(0.0, 2, 1, 3),
            [FeatureIds.PrizeUpgrade] = new FeaturePlacementConfig(0.0, 1, 1, 4),
        };
    }
}