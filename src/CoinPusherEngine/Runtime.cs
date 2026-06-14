namespace CoinPusherEngine;

public static class GameEngine
{
    public static ExecutionResult Execute(GameMasterPlan plan)
    {
        return ExecuteWithTrace(plan).Result;
    }

    public static ExecutionTraceResult ExecuteWithTrace(GameMasterPlan plan)
    {
        var totals = new Dictionary<int, int>();
        var boardHistory = new List<CellState?[,]>();
        var traces = new List<SpinExecutionTrace>();
        var board = BoardUtils.CloneBoard(plan.SpinPlans[0].BoardAtStart);

        for (var index = 0; index < plan.SpinPlans.Count; index++)
        {
            var spin = plan.SpinPlans[index];
            var nextSpin = index + 1 < plan.SpinPlans.Count ? plan.SpinPlans[index + 1] : null;
            var boardAtStart = BoardUtils.CloneBoard(board);

            FlattenStaleBoardFeatures(board);
            var boardAfterFlatten = BoardUtils.CloneBoard(board);
            ExecutePushers(board, spin.Pushers, plan.PrizeUpgradeMap, totals);
            var boardAfterPush = BoardUtils.CloneBoard(board);
            board = RotateBoard(board);
            var boardAfterRotate = BoardUtils.CloneBoard(board);
            ApplySpawns(board, spin.PlannedSpawns);
            var boardAfterSpawns = BoardUtils.CloneBoard(board);
            FireBoardFeatures(board, spin, nextSpin, plan.FillerSymIds.First());
            var boardAfterFeatures = BoardUtils.CloneBoard(board);
            boardHistory.Add(boardAfterFeatures);
            traces.Add(new SpinExecutionTrace
            {
                SpinNumber = spin.SpinNumber,
                IsExtraSpin = spin.IsExtraSpin,
                ParentTurnIndex = spin.ParentTurnIndex,
                BoardAtStart = boardAtStart,
                BoardAfterFlatten = boardAfterFlatten,
                BoardAfterPush = boardAfterPush,
                BoardAfterRotate = boardAfterRotate,
                BoardAfterSpawns = boardAfterSpawns,
                BoardAfterFeatures = boardAfterFeatures,
                TotalsAfterSpin = new Dictionary<int, int>(totals),
            });
        }

        var result = new ExecutionResult
        {
            Totals = totals,
            BoardHistory = boardHistory,
        };

        return new ExecutionTraceResult
        {
            Result = result,
            SpinTraces = traces,
        };
    }

    public static void FlattenStaleBoardFeatures(CellState?[,] board)
    {
        for (var row = 0; row < C.Rows; row++)
        {
            for (var col = 0; col < C.Cols; col++)
            {
                if (board[row, col]?.IsFeature == true)
                {
                    board[row, col] = board[row, col]!.ConvertToNormal();
                }
            }
        }
    }

    public static void ExecutePushers(
        CellState?[,] board,
        IReadOnlyList<PusherPlan> pushers,
        IReadOnlyDictionary<int, int>? upgradeMap,
        Dictionary<int, int> totals)
    {
        for (var col = 0; col < C.Cols; col++)
        {
            var pusher = pushers[col];
            if (pusher.IsFeature)
            {
                for (var row = 0; row < C.Rows; row++)
                {
                    CountCell(board[row, col], upgradeMap, totals);
                    board[row, col] = null;
                }

                continue;
            }

            var push = pusher.PushValue;
            for (var row = C.Rows - push; row < C.Rows; row++)
            {
                CountCell(board[row, col], upgradeMap, totals);
            }

            for (var row = C.Rows - 1; row >= 0; row--)
            {
                board[row, col] = row - push >= 0
                    ? board[row - push, col]
                    : null;
            }
        }
    }

    public static CellState?[,] RotateBoard(CellState?[,] board)
    {
        return BoardUtils.RotateClockwise(board);
    }

    private static void ApplySpawns(CellState?[,] board, IReadOnlyDictionary<BoardPosition, CellState> spawns)
    {
        foreach (var (position, cell) in spawns)
        {
            board[position.Row, position.Col] = cell.Clone();
        }
    }

    private static void FireBoardFeatures(
        CellState?[,] board,
        SpinPlan spin,
        SpinPlan? nextSpin,
        int fillerFallback)
    {
        var featurePositions = BoardUtils.EnumeratePositions()
            .Where(pos => board[pos.Row, pos.Col]?.IsFeature == true)
            .ToList();

        foreach (var pos in featurePositions)
        {
            var featureCell = board[pos.Row, pos.Col];
            if (featureCell is null || !featureCell.IsFeature)
            {
                continue;
            }

            switch (featureCell.FeatureId)
            {
                case FeatureIds.Wheel:
                    var targetSymId = featureCell.FParams?.WheelSymId
                        ?? throw new InvalidOperationException("WHEEL token missing target symbol.");
                    var stackMultiplier = featureCell.FParams?.WheelStackMultiplier ?? C.WheelStackMultiplier;
                    for (var row = 0; row < C.Rows; row++)
                    {
                        for (var col = 0; col < C.Cols; col++)
                        {
                            var current = board[row, col];
                            if (current is null || current.IsFeature || current.SymId != targetSymId)
                            {
                                continue;
                            }

                            board[row, col] = CellState.Normal(current.SymId, stackMultiplier);
                        }
                    }

                    board[pos.Row, pos.Col] = featureCell.ConvertToNormal();
                    if (nextSpin is not null)
                    {
                        PostWheelIsolation(board, targetSymId, nextSpin, fillerFallback);
                    }

                    break;

                case FeatureIds.ExtraSpin:
                case FeatureIds.PrizeUpgrade:
                    board[pos.Row, pos.Col] = featureCell.ConvertToNormal();
                    break;

                default:
                    board[pos.Row, pos.Col] = featureCell.ConvertToNormal();
                    break;
            }
        }
    }

    private static void PostWheelIsolation(
        CellState?[,] board,
        int wheelSymId,
        SpinPlan nextSpin,
        int fillerFallback)
    {
        foreach (var pos in BoardUtils.EnumeratePositions())
        {
            var cell = board[pos.Row, pos.Col];
            if (cell is null || cell.IsFeature || cell.SymId != wheelSymId)
            {
                continue;
            }

            var planned = nextSpin.BoardAtStart[pos.Row, pos.Col];
            var isPlanned = planned is not null && !planned.IsFeature && planned.SymId == wheelSymId;
            if (!nextSpin.Zones.Contains(pos) || !isPlanned)
            {
                board[pos.Row, pos.Col] = planned?.Clone() ?? CellState.Normal(fillerFallback);
            }
        }
    }

    private static void CountCell(
        CellState? cell,
        IReadOnlyDictionary<int, int>? upgradeMap,
        Dictionary<int, int> totals)
    {
        if (cell is null)
        {
            return;
        }

        var counted = cell.IsFeature
            ? cell.ConvertToNormal()
            : cell;
        if (counted.IsFeature)
        {
            return;
        }

        var symId = counted.SymId;
        if (upgradeMap is not null && upgradeMap.TryGetValue(symId, out var upgradedSymId))
        {
            symId = upgradedSymId;
        }

        totals[symId] = totals.GetValueOrDefault(symId) + counted.StackCount;
    }
}

public static class Verifier
{
    public static void VerifyPlan(GameMasterPlan plan, SymbolTable symbolTable, List<string>? planLog = null)
    {
        var traceResult = GameEngine.ExecuteWithTrace(plan);
        var result = traceResult.Result;

        for (var index = 0; index < plan.SpinPlans.Count - 1; index++)
        {
            var expected = plan.SpinPlans[index + 1].BoardAtStart;
            var actual = result.BoardHistory[index];
            if (!BoardUtils.BoardsEqual(actual, expected))
            {
                throw new InvalidOperationException(
                    $"Board mismatch after spin {index + 1}.{Environment.NewLine}Expected:{Environment.NewLine}{BoardToString(expected)}{Environment.NewLine}Actual:{Environment.NewLine}{BoardToString(actual)}");
            }
        }

        foreach (var (symId, target) in plan.TargetCollection)
        {
            var actual = result.Totals.GetValueOrDefault(symId);
            if (actual != target)
            {
                throw new InvalidOperationException($"{symbolTable.GetById(symId).Name} collected {actual}, threshold={target}");
            }
        }

        foreach (var fillerId in plan.FillerSymIds)
        {
            var maxCollections = symbolTable.GetById(fillerId).MaxCollections;
            if (!maxCollections.HasValue)
            {
                continue;
            }

            if (result.Totals.GetValueOrDefault(fillerId) >= maxCollections.Value)
            {
                throw new InvalidOperationException(
                    $"{symbolTable.GetById(fillerId).Name} collected {result.Totals.GetValueOrDefault(fillerId)}, limit={maxCollections.Value}");
            }
        }

        planLog?.Add("Verification: OK");
    }

    private static string BoardToString(CellState?[,] board)
    {
        var lines = new List<string>();
        for (var row = 0; row < C.Rows; row++)
        {
            var cells = new List<string>();
            for (var col = 0; col < C.Cols; col++)
            {
                var cell = board[row, col];
                cells.Add(cell is null
                    ? " . "
                    : cell.IsFeature
                        ? $"F{cell.SymId:00}"
                        : $"{cell.SymId:00}:{cell.StackCount}");
            }

            lines.Add(string.Join(" ", cells));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
