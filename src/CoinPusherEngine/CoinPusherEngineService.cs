namespace CoinPusherEngine;

public sealed class CoinPusherEngineService
{
    private readonly Planner _planner;

    public CoinPusherEngineService(
        SymbolTable? symbolTable = null,
        IReadOnlyDictionary<string, FeaturePlacementConfig>? featureRegistry = null)
    {
        _planner = new Planner(symbolTable, featureRegistry);
    }

    public GameMasterPlan GeneratePlan(MathInput input)
    {
        return _planner.GeneratePlan(input);
    }

    public string GenerateTicketJson(MathInput input)
    {
        var plan = GeneratePlan(input);
        return TicketSerializer.Serialize(plan);
    }
}
