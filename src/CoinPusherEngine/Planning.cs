using System.Collections.ObjectModel;

namespace CoinPusherEngine;

public static class BoardUtils
{
    public static CellState?[,] CreateEmptyBoard()
    {
        return new CellState?[C.Rows, C.Cols];
    }

    public static CellState?[,] CloneBoard(CellState?[,] board)
    {
        var clone = new CellState?[C.Rows, C.Cols];

        for (var row = 0; row < C.Rows; row++)
        {
            for (var col = 0; col < C.Cols; col++)
            {
                clone[row, col] = board[row, col]?.Clone();
            }
        }

        return clone;
    }

    public static CellState?[,] RotateClockwise(CellState?[,] board)
    {
        var rotated = new CellState?[C.Rows, C.Cols];

        for (var row = 0; row < C.Rows; row++)
        {
            for (var col = 0; col < C.Cols; col++)
            {
                rotated[col, C.Rows - 1 - row] = board[row, col]?.Clone();
            }
        }

        return rotated;
    }

    public static CellState?[,] RotateCounterClockwise(CellState?[,] board)
    {
        var rotated = new CellState?[C.Rows, C.Cols];

        for (var row = 0; row < C.Rows; row++)
        {
            for (var col = 0; col < C.Cols; col++)
            {
                rotated[C.Rows - 1 - col, row] = board[row, col]?.Clone();
            }
        }

        return rotated;
    }

    public static HashSet<BoardPosition> BuildZones(IReadOnlyList<PusherPlan> pushers)
    {
        var zones = new HashSet<BoardPosition>();

        for (var col = 0; col < C.Cols; col++)
        {
            var pusher = pushers[col];
            if (pusher.IsFeature)
            {
                for (var row = 0; row < C.Rows; row++)
                {
                    zones.Add(new BoardPosition(row, col));
                }

                continue;
            }

            for (var row = C.Rows - pusher.PushValue; row < C.Rows; row++)
            {
                zones.Add(new BoardPosition(row, col));
            }
        }

        return zones;
    }

    public static IEnumerable<BoardPosition> EnumeratePositions()
    {
        for (var row = 0; row < C.Rows; row++)
        {
            for (var col = 0; col < C.Cols; col++)
            {
                yield return new BoardPosition(row, col);
            }
        }
    }

    public static bool BoardsEqual(CellState?[,] left, CellState?[,] right)
    {
        for (var row = 0; row < C.Rows; row++)
        {
            for (var col = 0; col < C.Cols; col++)
            {
                var l = left[row, col];
                var r = right[row, col];
                if (l is null && r is null)
                {
                    continue;
                }

                if (l is null || r is null)
                {
                    return false;
                }

                if (l.SymId != r.SymId ||
                    l.StackCount != r.StackCount ||
                    l.IsFeature != r.IsFeature ||
                    !string.Equals(l.FeatureId, r.FeatureId, StringComparison.Ordinal) ||
                    l.ConvertSymId != r.ConvertSymId)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

public sealed partial class Planner
{
    private readonly SymbolTable _symbolTable;
    private readonly IReadOnlyDictionary<string, FeaturePlacementConfig> _featureRegistry;

    public Planner(
        SymbolTable? symbolTable = null,
        IReadOnlyDictionary<string, FeaturePlacementConfig>? featureRegistry = null)
    {
        _symbolTable = symbolTable ?? new SymbolTable();
        _featureRegistry = featureRegistry ?? SymbolTable.CreateDefaultFeatureRegistry();
    }

    public GameMasterPlan GeneratePlan(MathInput input)
    {
        var planLog = new List<string>();
        ValidateInput(input);

        var seed = input.Seed ?? Random.Shared.Next(1, int.MaxValue);
        var rng = new Random(seed);
        var symbols = BuildSymbolContext(input);
        var featureCounts = BuildFeatureCounts(input, symbols, rng, planLog);
        var placedFeatures = PlaceFeatures(input, symbols, featureCounts, rng, planLog);
        var wheelLocks = BuildWheelLocks(symbols, placedFeatures, planLog);
        var slotAllocations = ScheduleWinSlots(
            placedFeatures,
            symbols,
            wheelLocks,
            input.TotalBaseSpins + featureCounts.GetValueOrDefault(FeatureIds.ExtraSpin),
            planLog);
        var spinPlans = BuildSpinPlans(
            input,
            placedFeatures,
            symbols,
            wheelLocks,
            slotAllocations,
            rng,
            planLog);

        ResolveSpawns(spinPlans, symbols, planLog);

        var provisionalPlan = new GameMasterPlan
        {
            Seed = seed,
            TotalSpins = spinPlans.Count,
            SpinPlans = spinPlans,
            TargetCollection = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>(symbols.TargetById)),
            WinSymIds = symbols.WinSymIds.ToList(),
            FillerSymIds = symbols.FillerSymIds.ToList(),
            PrizeUpgradeMap = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>(symbols.PrizeUpgradeMap)),
            WheelLocks = wheelLocks,
            Verified = false,
            PlanLog = planLog,
        };

        Verifier.VerifyPlan(provisionalPlan, _symbolTable, planLog);

        return new GameMasterPlan
        {
            Seed = seed,
            TotalSpins = spinPlans.Count,
            SpinPlans = spinPlans,
            TargetCollection = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>(symbols.TargetById)),
            WinSymIds = symbols.WinSymIds.ToList(),
            FillerSymIds = symbols.FillerSymIds.ToList(),
            PrizeUpgradeMap = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>(symbols.PrizeUpgradeMap)),
            WheelLocks = wheelLocks,
            Verified = true,
            PlanLog = planLog,
        };
    }

    private void ValidateInput(MathInput input)
    {
        if (input.TotalBaseSpins <= 0)
        {
            throw new InvalidOperationException("TotalBaseSpins must be positive.");
        }

        if (input.TargetCollection.Count == 0)
        {
            throw new InvalidOperationException("TargetCollection must contain at least one symbol.");
        }

        foreach (var (name, target) in input.TargetCollection)
        {
            if (target <= 0)
            {
                throw new InvalidOperationException($"Target for {name} must be positive.");
            }

            var symbol = _symbolTable.GetByName(name);
            if (symbol.Category is SymbolCategory.FeatureToken or SymbolCategory.PusherFeature)
            {
                throw new InvalidOperationException($"TargetCollection cannot contain feature symbol {name}.");
            }
        }

        foreach (var featureName in input.RequiredFeatures.Keys)
        {
            if (!_featureRegistry.ContainsKey(featureName))
            {
                throw new InvalidOperationException($"Unknown feature '{featureName}'.");
            }
        }

        if (input.PrizeUpgradeMap is null)
        {
            return;
        }

        foreach (var (fromName, toName) in input.PrizeUpgradeMap)
        {
            var from = _symbolTable.GetByName(fromName);
            var to = _symbolTable.GetByName(toName);

            if (from.Category is SymbolCategory.FeatureToken or SymbolCategory.PusherFeature ||
                to.Category is SymbolCategory.FeatureToken or SymbolCategory.PusherFeature)
            {
                throw new InvalidOperationException("PrizeUpgradeMap must reference normal board symbols.");
            }

            if (from.Id == to.Id)
            {
                throw new InvalidOperationException("PrizeUpgradeMap cannot map a symbol to itself.");
            }
        }
    }

    private SymbolContext BuildSymbolContext(MathInput input)
    {
        var targetById = input.TargetCollection.ToDictionary(
            kvp => _symbolTable.GetByName(kvp.Key).Id,
            kvp => kvp.Value);

        var upgradeMap = new Dictionary<int, int>();
        var reverseUpgradeMap = new Dictionary<int, int>();

        if (input.PrizeUpgradeMap is not null)
        {
            foreach (var (fromName, toName) in input.PrizeUpgradeMap)
            {
                var fromId = _symbolTable.GetByName(fromName).Id;
                var toId = _symbolTable.GetByName(toName).Id;
                upgradeMap[fromId] = toId;
                reverseUpgradeMap[toId] = fromId;
            }
        }

        var winSymIds = targetById.Keys
            .OrderBy(id => id)
            .ToList();
        var fromSymIds = upgradeMap.Keys.ToHashSet();
        var fillerIds = _symbolTable.All
            .Where(def => def.Category is SymbolCategory.Win or SymbolCategory.FromSymbol or SymbolCategory.Filler)
            .Select(def => def.Id)
            .Where(id => !winSymIds.Contains(id) && !fromSymIds.Contains(id))
            .OrderBy(id => id)
            .ToList();

        return new SymbolContext
        {
            TargetById = new ReadOnlyDictionary<int, int>(targetById),
            WinSymIds = winSymIds,
            FillerSymIds = fillerIds,
            PrizeUpgradeMap = new ReadOnlyDictionary<int, int>(upgradeMap),
            ReversePrizeUpgradeMap = new ReadOnlyDictionary<int, int>(reverseUpgradeMap),
            PrizeUpgradeTargetIds = upgradeMap.Values.ToHashSet(),
            FromSymIds = fromSymIds,
        };
    }
}
