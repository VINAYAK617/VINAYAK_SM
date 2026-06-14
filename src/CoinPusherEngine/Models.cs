using System.Collections.ObjectModel;

namespace CoinPusherEngine;

public enum SymbolCategory
{
    Win,
    FromSymbol,
    Filler,
    FeatureToken,
    PusherFeature
}

public sealed record SymbolDefinition(int Id, string Name, string Glyph, SymbolCategory Category, int? MaxCollections = null);

public readonly record struct BoardPosition(int Row, int Col)
{
    public int ToFlatIndex() => (Row * C.Cols) + Col;
}

public sealed class CellState
{
    public int SymId { get; init; }
    public int StackCount { get; init; } = 1;
    public bool IsFeature { get; init; }
    public string? FeatureId { get; init; }
    public int ConvertSymId { get; init; }
    public FeatureParams? FParams { get; init; }

    public CellState Clone()
    {
        return new CellState
        {
            SymId = SymId,
            StackCount = StackCount,
            IsFeature = IsFeature,
            FeatureId = FeatureId,
            ConvertSymId = ConvertSymId,
            FParams = FParams?.Clone(),
        };
    }

    public CellState ConvertToNormal()
    {
        return new CellState
        {
            SymId = ConvertSymId,
            StackCount = 1,
            IsFeature = false,
            FeatureId = null,
            ConvertSymId = ConvertSymId,
            FParams = null,
        };
    }

    public static CellState Normal(int symId, int stackCount = 1)
    {
        return new CellState
        {
            SymId = symId,
            StackCount = stackCount,
            IsFeature = false,
            FeatureId = null,
            ConvertSymId = symId,
        };
    }

    public static CellState Feature(
        int symId,
        string featureId,
        int convertSymId,
        FeatureParams? featureParams = null)
    {
        return new CellState
        {
            SymId = symId,
            StackCount = 1,
            IsFeature = true,
            FeatureId = featureId,
            ConvertSymId = convertSymId,
            FParams = featureParams,
        };
    }
}

public sealed class FeatureParams
{
    public int? WheelSymId { get; init; }
    public int? WheelStackMultiplier { get; init; }
    public int? FromSymId { get; init; }
    public int? ToSymId { get; init; }
    public int? GrantedTurnIndex { get; init; }
    public int Depth { get; init; }
    public List<FeatureParams> Children { get; init; } = new();

    public FeatureParams Clone()
    {
        return new FeatureParams
        {
            WheelSymId = WheelSymId,
            WheelStackMultiplier = WheelStackMultiplier,
            FromSymId = FromSymId,
            ToSymId = ToSymId,
            GrantedTurnIndex = GrantedTurnIndex,
            Depth = Depth,
            Children = Children.Select(child => child.Clone()).ToList(),
        };
    }
}

public sealed record FeaturePlacementConfig(
    double Probability,
    int MaxInstances,
    int MinSpin,
    int PlacementOrder);

public sealed class MathInput
{
    public IReadOnlyDictionary<string, int> TargetCollection { get; init; } = new Dictionary<string, int>();
    public int TotalBaseSpins { get; init; }
    public IReadOnlyDictionary<string, int> RequiredFeatures { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string>? WheelSymbolOrder { get; init; }
    public IReadOnlyDictionary<string, string>? PrizeUpgradeMap { get; init; }
    public int? Seed { get; init; }
}

public sealed class PlacementContext
{
    public required MathInput Input { get; init; }
    public required SymbolContext Symbols { get; init; }
    public required Random Rng { get; init; }
    public required IReadOnlyList<ResolvedFeature> PlacedFeatures { get; init; }
    public required int TotalSpins { get; init; }
}

public sealed class SymbolContext
{
    public required IReadOnlyDictionary<int, int> TargetById { get; init; }
    public required IReadOnlyList<int> WinSymIds { get; init; }
    public required IReadOnlyList<int> FillerSymIds { get; init; }
    public required IReadOnlyDictionary<int, int> PrizeUpgradeMap { get; init; }
    public required IReadOnlyDictionary<int, int> ReversePrizeUpgradeMap { get; init; }
    public required IReadOnlyCollection<int> PrizeUpgradeTargetIds { get; init; }
    public required IReadOnlyCollection<int> FromSymIds { get; init; }
}

public sealed class ResolvedFeature
{
    public required string FeatureId { get; init; }
    public required int FeatureSymId { get; init; }
    public required int SpinNumber { get; init; }
    public required int Column { get; init; }
    public int? TargetSymbolId { get; set; }
    public int? StackMultiplier { get; set; }
    public int? GrantedSpinNumber { get; set; }
    public int Depth { get; set; }
}

public sealed class WheelLock
{
    public required int SymbolId { get; init; }
    public required int WheelSpin { get; init; }
    public required int StackMultiplier { get; init; }
    public required int ZoneCells { get; init; }
    public required int PreWins { get; init; }
    public required int PostWins { get; init; }
}

public sealed class PusherPlan
{
    public required int PushValue { get; init; }
    public int? FeatureId { get; init; }
    public bool IsFeature => FeatureId.HasValue;
}

public sealed class FeatureSpawn
{
    public required string FeatureId { get; init; }
    public required int SymId { get; init; }
    public required int PreferredColumn { get; init; }
    public int? Row { get; set; }
    public int? Col { get; set; }
    public int ConvertToId { get; set; }
    public required FeatureParams Params { get; init; }
}

public sealed class SpinPlan
{
    public required int SpinNumber { get; init; }
    public required bool IsExtraSpin { get; init; }
    public int? ParentTurnIndex { get; init; }
    public required CellState?[,] BoardAtStart { get; init; }
    public required IReadOnlyList<PusherPlan> Pushers { get; init; }
    public required Dictionary<BoardPosition, CellState> PlannedSpawns { get; init; }
    public required IReadOnlyList<FeatureSpawn> FeatureSpawns { get; init; }
    public required IReadOnlyDictionary<int, int> SlotAllocations { get; init; }
    public required IReadOnlySet<BoardPosition> Zones { get; init; }

    public SpinPlan CloneWithoutSpawns()
    {
        return new SpinPlan
        {
            SpinNumber = SpinNumber,
            IsExtraSpin = IsExtraSpin,
            ParentTurnIndex = ParentTurnIndex,
            BoardAtStart = BoardUtils.CloneBoard(BoardAtStart),
            Pushers = Pushers.Select(p => new PusherPlan { PushValue = p.PushValue, FeatureId = p.FeatureId }).ToList(),
            PlannedSpawns = new Dictionary<BoardPosition, CellState>(),
            FeatureSpawns = FeatureSpawns
                .Select(spawn => new FeatureSpawn
                {
                    FeatureId = spawn.FeatureId,
                    SymId = spawn.SymId,
                    PreferredColumn = spawn.PreferredColumn,
                    Row = spawn.Row,
                    Col = spawn.Col,
                    ConvertToId = spawn.ConvertToId,
                    Params = spawn.Params.Clone(),
                }).ToList(),
            SlotAllocations = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>(SlotAllocations)),
            Zones = new HashSet<BoardPosition>(Zones),
        };
    }
}

public sealed class GameMasterPlan
{
    public required int Seed { get; init; }
    public required int TotalSpins { get; init; }
    public required IReadOnlyList<SpinPlan> SpinPlans { get; init; }
    public required IReadOnlyDictionary<int, int> TargetCollection { get; init; }
    public required IReadOnlyList<int> WinSymIds { get; init; }
    public required IReadOnlyList<int> FillerSymIds { get; init; }
    public required IReadOnlyDictionary<int, int> PrizeUpgradeMap { get; init; }
    public required IReadOnlyList<WheelLock> WheelLocks { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> PlanLog { get; init; }
}

public sealed class ExecutionResult
{
    public required IReadOnlyDictionary<int, int> Totals { get; init; }
    public required List<CellState?[,]> BoardHistory { get; init; }
}
