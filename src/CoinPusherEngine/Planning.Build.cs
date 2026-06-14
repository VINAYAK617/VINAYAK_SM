using System.Collections.ObjectModel;

namespace CoinPusherEngine;

public sealed partial class Planner
{
    private Dictionary<int, Dictionary<int, int>> ScheduleWinSlots(
        IReadOnlyList<ResolvedFeature> placedFeatures,
        SymbolContext symbols,
        IReadOnlyList<WheelLock> wheelLocks,
        int totalSpins,
        List<string> planLog)
    {
        var allocations = Enumerable.Range(1, totalSpins)
            .ToDictionary(spin => spin, _ => new Dictionary<int, int>());
        var capacityRemaining = Enumerable.Range(1, totalSpins)
            .ToDictionary(spin => spin, spin => ComputeSpinCapacity(spin, placedFeatures, wheelLocks));

        foreach (var wheelLock in wheelLocks)
        {
            var boardSymId = ResolveBoardSymbolId(symbols, wheelLock.SymbolId);
            AddAllocation(allocations[wheelLock.WheelSpin + 1], boardSymId, wheelLock.ZoneCells);
            capacityRemaining[wheelLock.WheelSpin + 1] -= wheelLock.ZoneCells;
        }

        foreach (var wheelLock in wheelLocks.OrderBy(lockItem => lockItem.WheelSpin))
        {
            var boardSymId = ResolveBoardSymbolId(symbols, wheelLock.SymbolId);
            var remaining = wheelLock.PreWins;

            for (var spin = 1; spin < wheelLock.WheelSpin && remaining > 0; spin++)
            {
                var take = Math.Min(capacityRemaining[spin], remaining);
                if (take <= 0)
                {
                    continue;
                }

                AddAllocation(allocations[spin], boardSymId, take);
                capacityRemaining[spin] -= take;
                remaining -= take;
            }

            if (remaining > 0)
            {
                throw new InvalidOperationException($"EDF scheduling failed for {_symbolTable.GetById(wheelLock.SymbolId).Name}.");
            }
        }

        var wheeledSymbols = wheelLocks.Select(lockItem => lockItem.SymbolId).ToHashSet();
        foreach (var (winSymId, target) in symbols.TargetById.OrderByDescending(kvp => kvp.Value))
        {
            if (wheeledSymbols.Contains(winSymId))
            {
                continue;
            }

            var boardSymId = ResolveBoardSymbolId(symbols, winSymId);
            var remaining = target;

            foreach (var spin in capacityRemaining.OrderByDescending(kvp => kvp.Key).Select(kvp => kvp.Key))
            {
                if (remaining <= 0)
                {
                    break;
                }

                var take = Math.Min(capacityRemaining[spin], remaining);
                if (take <= 0)
                {
                    continue;
                }

                AddAllocation(allocations[spin], boardSymId, take);
                capacityRemaining[spin] -= take;
                remaining -= take;
            }

            if (remaining > 0)
            {
                planLog.Add($"WARNING: unplaced wins for {_symbolTable.GetById(winSymId).Name}; force placing overflow.");
                AddAllocation(allocations[totalSpins], boardSymId, remaining);
            }
        }

        return allocations;
    }

    private int ComputeSpinCapacity(
        int spin,
        IReadOnlyList<ResolvedFeature> features,
        IReadOnlyList<WheelLock> wheelLocks)
    {
        var flushCount = features.Count(feature => feature.FeatureId == FeatureIds.Flush && feature.SpinNumber == spin);
        var freeCols = C.Cols - flushCount;
        var isWheelSpin = wheelLocks.Any(lockItem => lockItem.WheelSpin == spin);
        return (flushCount * C.Rows) + (freeCols * (isWheelSpin ? C.MinPush : C.MaxPush));
    }

    private static void AddAllocation(Dictionary<int, int> allocations, int symId, int count)
    {
        if (count <= 0)
        {
            return;
        }

        allocations[symId] = allocations.GetValueOrDefault(symId) + count;
    }

    private int ResolveBoardSymbolId(SymbolContext symbols, int winSymId)
    {
        return symbols.ReversePrizeUpgradeMap.TryGetValue(winSymId, out var fromSymId)
            ? fromSymId
            : winSymId;
    }

    private List<SpinPlan> BuildSpinPlans(
        MathInput input,
        IReadOnlyList<ResolvedFeature> placedFeatures,
        SymbolContext symbols,
        IReadOnlyList<WheelLock> wheelLocks,
        Dictionary<int, Dictionary<int, int>> slotAllocations,
        Random rng,
        List<string> planLog)
    {
        var totalSpins = input.TotalBaseSpins + placedFeatures.Count(feature => feature.FeatureId == FeatureIds.ExtraSpin);
        var extraSpinParents = placedFeatures
            .Where(feature => feature.FeatureId == FeatureIds.ExtraSpin && feature.GrantedSpinNumber.HasValue)
            .OrderBy(feature => feature.GrantedSpinNumber)
            .ToDictionary(feature => feature.GrantedSpinNumber!.Value, feature => feature.SpinNumber - 1);
        var pushersBySpin = Enumerable.Range(1, totalSpins)
            .ToDictionary(spin => spin, spin => ComputePushers(spin, placedFeatures, slotAllocations[spin], wheelLocks));
        var boards = new Dictionary<int, CellState?[,]>();

        for (var spin = totalSpins; spin >= 1; spin--)
        {
            var nextBoard = spin == totalSpins
                ? BoardUtils.CreateEmptyBoard()
                : boards[spin + 1];
            var board = BoardUtils.RotateCounterClockwise(nextBoard);

            UndoPush(board, pushersBySpin[spin]);
            FillZone(board, pushersBySpin[spin], slotAllocations[spin], symbols, wheelLocks, spin);
            IsolateWheelSymbols(board, pushersBySpin[spin], wheelLocks, spin);
            FillFillers(board, symbols, rng);
            boards[spin] = board;
        }

        var spins = new List<SpinPlan>();
        for (var spin = 1; spin <= totalSpins; spin++)
        {
            var featureSpawns = BuildFeatureSpawnsForSpin(spin, placedFeatures, symbols);
            spins.Add(new SpinPlan
            {
                SpinNumber = spin,
                IsExtraSpin = spin > input.TotalBaseSpins,
                ParentTurnIndex = extraSpinParents.GetValueOrDefault(spin),
                BoardAtStart = boards[spin],
                Pushers = pushersBySpin[spin],
                PlannedSpawns = new Dictionary<BoardPosition, CellState>(),
                FeatureSpawns = featureSpawns,
                SlotAllocations = new ReadOnlyDictionary<int, int>(slotAllocations[spin]),
                Zones = BoardUtils.BuildZones(pushersBySpin[spin]),
            });
        }

        foreach (var spinPlan in spins)
        {
            planLog.Add(
                $"Spin {spinPlan.SpinNumber}: slots={spinPlan.SlotAllocations.Values.Sum()} push=[{string.Join(",", spinPlan.Pushers.Select(p => p.PushValue))}]");
        }

        return spins;
    }

    private List<PusherPlan> ComputePushers(
        int spin,
        IReadOnlyList<ResolvedFeature> placedFeatures,
        IReadOnlyDictionary<int, int> allocations,
        IReadOnlyList<WheelLock> wheelLocks)
    {
        var flushColumns = placedFeatures
            .Where(feature => feature.FeatureId == FeatureIds.Flush && feature.SpinNumber == spin)
            .Select(feature => feature.Column)
            .ToHashSet();
        var pushers = new List<PusherPlan>(C.Cols);
        var freeColumns = Enumerable.Range(0, C.Cols)
            .Where(col => !flushColumns.Contains(col))
            .ToList();
        var totalWins = allocations.Values.Sum();
        var flushCap = flushColumns.Count * C.Rows;
        var isWheelSpin = wheelLocks.Any(lockItem => lockItem.WheelSpin == spin);

        var needed = isWheelSpin
            ? freeColumns.Count * C.MinPush
            : Math.Max(freeColumns.Count * C.MinPush, totalWins - flushCap);
        var pushValues = freeColumns.ToDictionary(col => col, _ => C.MinPush);
        var remaining = Math.Max(0, needed - (freeColumns.Count * C.MinPush));
        var fillIndex = 0;

        while (remaining > 0 && freeColumns.Count > 0)
        {
            var col = freeColumns[fillIndex % freeColumns.Count];
            if (pushValues[col] < C.MaxPush)
            {
                pushValues[col]++;
                remaining--;
            }

            fillIndex++;
        }

        for (var col = 0; col < C.Cols; col++)
        {
            if (flushColumns.Contains(col))
            {
                pushers.Add(new PusherPlan { PushValue = C.FlushPush, FeatureId = 13 });
                continue;
            }

            pushers.Add(new PusherPlan { PushValue = pushValues[col] });
        }

        return pushers;
    }

    private static void UndoPush(CellState?[,] board, IReadOnlyList<PusherPlan> pushers)
    {
        for (var col = 0; col < C.Cols; col++)
        {
            var pusher = pushers[col];
            var rebuilt = new CellState?[C.Rows];

            if (pusher.IsFeature)
            {
                for (var row = 0; row < C.Rows; row++)
                {
                    rebuilt[row] = null;
                }
            }
            else
            {
                var push = pusher.PushValue;
                for (var row = 0; row < C.Rows - push; row++)
                {
                    rebuilt[row] = board[row + push, col]?.Clone();
                }
            }

            for (var row = 0; row < C.Rows; row++)
            {
                board[row, col] = rebuilt[row];
            }
        }
    }

    private void FillZone(
        CellState?[,] board,
        IReadOnlyList<PusherPlan> pushers,
        IReadOnlyDictionary<int, int> allocations,
        SymbolContext symbols,
        IReadOnlyList<WheelLock> wheelLocks,
        int spin)
    {
        var zones = BoardUtils.BuildZones(pushers);
        var safeZone = zones
            .Where(pos => pos.Col != C.Cols - 1)
            .OrderBy(pos => pos.Col)
            .ThenBy(pos => pos.Row)
            .ToList();
        var spawnZone = zones
            .Where(pos => pos.Col == C.Cols - 1)
            .OrderBy(pos => pos.Row)
            .ToList();
        var queue = new Queue<BoardPosition>(safeZone.Concat(spawnZone));
        var wheelSymbols = wheelLocks
            .Where(lockItem => lockItem.WheelSpin + 1 == spin)
            .Select(lockItem => ResolveBoardSymbolId(symbols, lockItem.SymbolId))
            .Distinct()
            .ToHashSet();
        var symbolOrder = allocations.Keys
            .OrderBy(symId => wheelSymbols.Contains(symId) ? 0 : 1)
            .ThenByDescending(symId => allocations[symId])
            .ThenBy(symId => symId)
            .ToList();

        foreach (var symId in symbolOrder)
        {
            var count = allocations[symId];
            for (var idx = 0; idx < count; idx++)
            {
                if (queue.Count == 0)
                {
                    throw new InvalidOperationException($"Zone overflow at spin {spin}.");
                }

                var slot = queue.Dequeue();
                var stackCount = wheelSymbols.Contains(symId)
                    ? C.WheelStackMultiplier
                    : 1;
                board[slot.Row, slot.Col] = CellState.Normal(symId, stackCount);
            }
        }
    }

    private void IsolateWheelSymbols(
        CellState?[,] board,
        IReadOnlyList<PusherPlan> pushers,
        IReadOnlyList<WheelLock> wheelLocks,
        int spin)
    {
        var zones = BoardUtils.BuildZones(pushers);
        foreach (var wheelLock in wheelLocks)
        {
            if (wheelLock.WheelSpin == spin)
            {
                foreach (var pos in zones)
                {
                    if (board[pos.Row, pos.Col]?.SymId == wheelLock.SymbolId)
                    {
                        board[pos.Row, pos.Col] = null;
                    }
                }
            }

            if (wheelLock.WheelSpin + 1 == spin)
            {
                foreach (var pos in BoardUtils.EnumeratePositions())
                {
                    if (zones.Contains(pos))
                    {
                        continue;
                    }

                    if (board[pos.Row, pos.Col]?.SymId == wheelLock.SymbolId)
                    {
                        board[pos.Row, pos.Col] = null;
                    }
                }
            }
        }
    }

    private void FillFillers(CellState?[,] board, SymbolContext symbols, Random rng)
    {
        for (var row = 0; row < C.Rows; row++)
        {
            for (var col = 0; col < C.Cols; col++)
            {
                if (board[row, col] is not null)
                {
                    continue;
                }

                var fillerSymId = symbols.FillerSymIds[rng.Next(symbols.FillerSymIds.Count)];
                board[row, col] = CellState.Normal(fillerSymId);
            }
        }
    }

    private List<FeatureSpawn> BuildFeatureSpawnsForSpin(
        int spin,
        IReadOnlyList<ResolvedFeature> features,
        SymbolContext symbols)
    {
        var featureSpawns = new List<FeatureSpawn>();
        var prizeUpgradePair = symbols.PrizeUpgradeMap.FirstOrDefault();

        foreach (var feature in features.Where(item => item.SpinNumber == spin && item.FeatureId != FeatureIds.Flush))
        {
            switch (feature.FeatureId)
            {
                case FeatureIds.Wheel:
                    featureSpawns.Add(new FeatureSpawn
                    {
                        FeatureId = FeatureIds.Wheel,
                        SymId = 11,
                        PreferredColumn = feature.Column,
                        Params = new FeatureParams
                        {
                            WheelSymId = feature.TargetSymbolId,
                            WheelStackMultiplier = feature.StackMultiplier,
                            Depth = feature.Depth,
                        },
                    });
                    break;

                case FeatureIds.ExtraSpin:
                    featureSpawns.Add(new FeatureSpawn
                    {
                        FeatureId = FeatureIds.ExtraSpin,
                        SymId = 12,
                        PreferredColumn = feature.Column,
                        Params = new FeatureParams
                        {
                            GrantedTurnIndex = feature.GrantedSpinNumber.HasValue
                                ? feature.GrantedSpinNumber.Value - 1
                                : null,
                            Depth = 0,
                        },
                    });
                    break;

                case FeatureIds.PrizeUpgrade:
                    featureSpawns.Add(new FeatureSpawn
                    {
                        FeatureId = FeatureIds.PrizeUpgrade,
                        SymId = 14,
                        PreferredColumn = feature.Column,
                        Params = new FeatureParams
                        {
                            FromSymId = prizeUpgradePair.Key == 0 ? null : prizeUpgradePair.Key,
                            ToSymId = prizeUpgradePair.Value == 0 ? null : prizeUpgradePair.Value,
                            Depth = feature.Depth,
                        },
                    });
                    break;
            }
        }

        return featureSpawns;
    }

    private void ResolveSpawns(
        IReadOnlyList<SpinPlan> spinPlans,
        SymbolContext symbols,
        List<string> planLog)
    {
        for (var idx = 0; idx < spinPlans.Count - 1; idx++)
        {
            var current = spinPlans[idx];
            var next = spinPlans[idx + 1];
            var sim = SimulatePushRotate(current);

            foreach (var pos in BoardUtils.EnumeratePositions())
            {
                var nextCell = next.BoardAtStart[pos.Row, pos.Col];
                if (nextCell is null)
                {
                    continue;
                }

                var simCell = sim[pos.Row, pos.Col];
                var isZone = next.Zones.Contains(pos);
                var simIsFiller = simCell is null || simCell.IsFeature || symbols.FillerSymIds.Contains(simCell.SymId);
                var planIsWin = !nextCell.IsFeature && !symbols.FillerSymIds.Contains(nextCell.SymId);
                var shouldSpawn = simCell is null || (isZone && simIsFiller && planIsWin);

                if (shouldSpawn)
                {
                    current.PlannedSpawns[pos] = nextCell.Clone();
                }
            }

            PlaceTokens(current, symbols, planLog);
        }
    }

    private static CellState?[,] SimulatePushRotate(SpinPlan spinPlan)
    {
        var board = BoardUtils.CloneBoard(spinPlan.BoardAtStart);
        GameEngine.FlattenStaleBoardFeatures(board);
        GameEngine.ExecutePushers(board, spinPlan.Pushers, null, new Dictionary<int, int>());
        return BoardUtils.RotateClockwise(board);
    }

    private void PlaceTokens(SpinPlan spinPlan, SymbolContext symbols, List<string> planLog)
    {
        var usedSlots = new HashSet<BoardPosition>();

        foreach (var featureSpawn in spinPlan.FeatureSpawns)
        {
            var slot = FindSlot(spinPlan.PlannedSpawns, featureSpawn.PreferredColumn, symbols, usedSlots);
            if (slot is null)
            {
                planLog.Add($"WARNING: no spawn slot for {featureSpawn.FeatureId} Spin {spinPlan.SpinNumber}");
                continue;
            }

            var position = slot.Value;
            var displaced = spinPlan.PlannedSpawns[position];
            featureSpawn.ConvertToId = displaced.SymId;
            featureSpawn.Row = position.Row;
            featureSpawn.Col = position.Col;
            usedSlots.Add(position);
            spinPlan.PlannedSpawns[position] = CellState.Feature(
                featureSpawn.SymId,
                featureSpawn.FeatureId,
                displaced.SymId,
                featureSpawn.Params.Clone());
            planLog.Add(
                $"Spawn token {featureSpawn.FeatureId} spin={spinPlan.SpinNumber} pos={position.ToFlatIndex()} convert={displaced.SymId}");
        }
    }

    private static BoardPosition? FindSlot(
        Dictionary<BoardPosition, CellState> spawns,
        int preferredColumn,
        SymbolContext symbols,
        IReadOnlySet<BoardPosition> usedSlots)
    {
        var fillerCandidates = spawns
            .Where(kvp => !usedSlots.Contains(kvp.Key) && !kvp.Value.IsFeature && !IsWinLikeCell(kvp.Value, symbols))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        if (TryPickPreferredSlot(fillerCandidates.Keys, preferredColumn, out var fillerSlot))
        {
            return fillerSlot;
        }

        var fallbackCandidates = spawns.Keys
            .Where(key => !usedSlots.Contains(key) && !spawns[key].IsFeature);
        if (TryPickPreferredSlot(fallbackCandidates, preferredColumn, out var fallbackSlot))
        {
            return fallbackSlot;
        }

        return null;
    }

    private static bool TryPickPreferredSlot(
        IEnumerable<BoardPosition> candidates,
        int preferredColumn,
        out BoardPosition slot)
    {
        var candidateList = candidates.ToList();
        var preferred = new BoardPosition(preferredColumn, C.Cols - 1);
        if (candidateList.Contains(preferred))
        {
            slot = preferred;
            return true;
        }

        var rowMatch = candidateList
            .Where(pos => pos.Row == preferredColumn)
            .OrderByDescending(pos => pos.Col)
            .FirstOrDefault();
        if (candidateList.Contains(rowMatch))
        {
            slot = rowMatch;
            return true;
        }

        var highestColumn = candidateList
            .OrderByDescending(pos => pos.Col)
            .ThenBy(pos => pos.Row)
            .FirstOrDefault();
        if (candidateList.Contains(highestColumn))
        {
            slot = highestColumn;
            return true;
        }

        slot = default;
        return false;
    }

    private static bool IsWinLikeCell(CellState cell, SymbolContext symbols)
    {
        return !cell.IsFeature && !symbols.FillerSymIds.Contains(cell.SymId);
    }
}
