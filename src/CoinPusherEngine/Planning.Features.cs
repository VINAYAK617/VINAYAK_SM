namespace CoinPusherEngine;

public sealed partial class Planner
{
    private Dictionary<string, int> BuildFeatureCounts(
        MathInput input,
        SymbolContext symbols,
        Random rng,
        List<string> planLog)
    {
        var exactCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var required = new Dictionary<string, int>(input.RequiredFeatures, StringComparer.OrdinalIgnoreCase);

        if (required.Count == 0)
        {
            var autoFeatures = ComputeAutoFeatures(input, symbols);
            foreach (var (featureId, count) in autoFeatures)
            {
                if (count > 0)
                {
                    required[featureId] = count;
                }
            }

            if (autoFeatures.Count > 0)
            {
                planLog.Add(
                    $"Auto features: {string.Join(", ", autoFeatures.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
            }
        }

        foreach (var (featureId, config) in _featureRegistry.OrderBy(kvp => kvp.Value.PlacementOrder))
        {
            var requiredCount = required.GetValueOrDefault(featureId);
            if (requiredCount > 0)
            {
                exactCounts[featureId] = requiredCount;
                continue;
            }

            var count = 0;
            for (var idx = 0; idx < config.MaxInstances; idx++)
            {
                if (rng.NextDouble() < config.Probability)
                {
                    count++;
                }
            }

            exactCounts[featureId] = count;
        }

        return exactCounts;
    }

    private Dictionary<string, int> ComputeAutoFeatures(MathInput input, SymbolContext symbols)
    {
        var totals = symbols.TargetById.Values.Sum();
        var naiveCapacity = input.TotalBaseSpins * C.Cols * C.MaxPush;

        if (totals <= naiveCapacity)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var orderedSymbols = OrderWheelCandidates(input, symbols).ToList();
        var wheelCount = 0;
        var extraSpinCount = 0;

        while (true)
        {
            var totalSpins = input.TotalBaseSpins + extraSpinCount;
            var slotCapacity = (wheelCount * C.Cols * C.MinPush) + ((totalSpins - wheelCount) * C.Cols * C.MaxPush);
            var slotUsage = ComputeSlotUsage(symbols, orderedSymbols.Take(wheelCount));

            if (slotUsage <= slotCapacity)
            {
                break;
            }

            var maxWheels = Math.Max(0, (totalSpins - 1) / 2);
            if (wheelCount < maxWheels && wheelCount < orderedSymbols.Count)
            {
                wheelCount++;
                continue;
            }

            extraSpinCount++;
            if (extraSpinCount > _featureRegistry[FeatureIds.ExtraSpin].MaxInstances)
            {
                throw new InvalidOperationException("Targets exceed capacity even after auto features.");
            }
        }

        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [FeatureIds.Wheel] = wheelCount,
            [FeatureIds.ExtraSpin] = extraSpinCount,
        };
    }

    private int ComputeSlotUsage(SymbolContext symbols, IEnumerable<int> wheeledSymbols)
    {
        var usage = 0;
        var wheeled = wheeledSymbols.ToHashSet();

        foreach (var (symId, target) in symbols.TargetById)
        {
            if (!wheeled.Contains(symId))
            {
                usage += target;
                continue;
            }

            var zoneCells = Math.Min(target / C.WheelStackMultiplier, C.Cols - 1);
            usage += (target - (zoneCells * C.WheelStackMultiplier)) + zoneCells;
        }

        return usage;
    }

    private List<ResolvedFeature> PlaceFeatures(
        MathInput input,
        SymbolContext symbols,
        Dictionary<string, int> featureCounts,
        Random rng,
        List<string> planLog)
    {
        var placed = new List<ResolvedFeature>();
        var extraSpinCount = featureCounts.GetValueOrDefault(FeatureIds.ExtraSpin);
        var totalSpins = input.TotalBaseSpins + extraSpinCount;

        for (var idx = 0; idx < extraSpinCount; idx++)
        {
            var grantedSpinNumber = input.TotalBaseSpins + idx + 1;
            TryPlaceFeature(
                FeatureIds.ExtraSpin,
                allowExtraSpinPlacement: false,
                count: 1,
                input,
                symbols,
                totalSpins,
                rng,
                placed,
                planLog,
                grantedSpinNumber);
        }

        foreach (var featureId in new[] { FeatureIds.Wheel, FeatureIds.Flush, FeatureIds.PrizeUpgrade })
        {
            TryPlaceFeature(
                featureId,
                allowExtraSpinPlacement: true,
                count: featureCounts.GetValueOrDefault(featureId),
                input,
                symbols,
                totalSpins,
                rng,
                placed,
                planLog,
                grantedSpinNumber: null);
        }

        return placed
            .OrderBy(feature => feature.SpinNumber)
            .ThenBy(feature => feature.Column)
            .ThenBy(feature => _featureRegistry[feature.FeatureId].PlacementOrder)
            .ToList();
    }

    private void TryPlaceFeature(
        string featureId,
        bool allowExtraSpinPlacement,
        int count,
        MathInput input,
        SymbolContext symbols,
        int totalSpins,
        Random rng,
        List<ResolvedFeature> placed,
        List<string> planLog,
        int? grantedSpinNumber)
    {
        if (count <= 0)
        {
            return;
        }

        for (var instance = 0; instance < count; instance++)
        {
            var placedThisInstance = false;

            for (var attempt = 0; attempt < C.MaxPlacementAttempts; attempt++)
            {
                var minSpin = _featureRegistry[featureId].MinSpin;
                var spinUpperBound = featureId switch
                {
                    FeatureIds.ExtraSpin => input.TotalBaseSpins,
                    FeatureIds.Flush => allowExtraSpinPlacement ? totalSpins + 1 : input.TotalBaseSpins + 1,
                    _ => allowExtraSpinPlacement ? totalSpins : input.TotalBaseSpins,
                };

                if (spinUpperBound <= minSpin)
                {
                    continue;
                }

                var maxColExclusive = featureId.Equals(FeatureIds.Flush, StringComparison.OrdinalIgnoreCase)
                    ? C.Cols
                    : C.Cols - 1;

                var spin = rng.Next(minSpin, spinUpperBound);
                var col = rng.Next(0, maxColExclusive);
                var feature = TryBuildFeature(
                    featureId,
                    spin,
                    col,
                    input,
                    symbols,
                    totalSpins,
                    grantedSpinNumber,
                    placed);

                if (feature is null)
                {
                    continue;
                }

                placed.Add(feature);
                planLog.Add(
                    $"Placed {featureId} spin={feature.SpinNumber} col={feature.Column}" +
                    $"{(feature.TargetSymbolId.HasValue ? $" target={_symbolTable.GetById(feature.TargetSymbolId.Value).Name}" : string.Empty)}");
                placedThisInstance = true;
                break;
            }

            if (!placedThisInstance)
            {
                planLog.Add($"WARNING: could not place required {featureId} {instance + 1}/{count}");
            }
        }
    }

    private ResolvedFeature? TryBuildFeature(
        string featureId,
        int spin,
        int col,
        MathInput input,
        SymbolContext symbols,
        int totalSpins,
        int? grantedSpinNumber,
        IReadOnlyList<ResolvedFeature> placed)
    {
        if (placed.Any(existing => existing.SpinNumber == spin && existing.Column == col && existing.FeatureId.Equals(featureId, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var depth = spin > input.TotalBaseSpins ? 1 : 0;
        switch (featureId)
        {
            case FeatureIds.ExtraSpin:
                if (grantedSpinNumber is null || spin < 1 || spin >= input.TotalBaseSpins || col >= C.Cols - 1)
                {
                    return null;
                }

                return new ResolvedFeature
                {
                    FeatureId = FeatureIds.ExtraSpin,
                    FeatureSymId = 12,
                    SpinNumber = spin,
                    Column = col,
                    GrantedSpinNumber = grantedSpinNumber,
                    Depth = 0,
                };

            case FeatureIds.Flush:
                if (placed.Any(existing => existing.FeatureId == FeatureIds.Flush && existing.SpinNumber == spin && existing.Column == col))
                {
                    return null;
                }

                return new ResolvedFeature
                {
                    FeatureId = FeatureIds.Flush,
                    FeatureSymId = 13,
                    SpinNumber = spin,
                    Column = col,
                    Depth = depth,
                };

            case FeatureIds.PrizeUpgrade:
                if (symbols.PrizeUpgradeMap.Count == 0 || col >= C.Cols - 1)
                {
                    return null;
                }

                if (placed.Count(existing => existing.FeatureId == FeatureIds.PrizeUpgrade) >= _featureRegistry[FeatureIds.PrizeUpgrade].MaxInstances)
                {
                    return null;
                }

                return new ResolvedFeature
                {
                    FeatureId = FeatureIds.PrizeUpgrade,
                    FeatureSymId = 14,
                    SpinNumber = spin,
                    Column = col,
                    Depth = depth,
                };

            case FeatureIds.Wheel:
                if (spin < _featureRegistry[FeatureIds.Wheel].MinSpin || spin >= totalSpins || col >= C.Cols - 1)
                {
                    return null;
                }

                var targetSymbolId = PickWheelTarget(input, symbols, placed);
                if (targetSymbolId is null)
                {
                    return null;
                }

                var lockCandidate = BuildWheelLock(targetSymbolId.Value, spin, symbols.TargetById[targetSymbolId.Value]);
                if (lockCandidate.ZoneCells <= 0)
                {
                    return null;
                }

                if (placed.Any(existing => existing.FeatureId == FeatureIds.Wheel &&
                                           existing.TargetSymbolId == targetSymbolId &&
                                           spin <= existing.SpinNumber + 1 &&
                                           spin >= existing.SpinNumber - 1))
                {
                    return null;
                }

                var existingLocks = BuildWheelLocks(symbols, placed, new List<string>());
                var zoneCellsAtNextSpin = existingLocks
                    .Where(lockItem => lockItem.WheelSpin + 1 == spin + 1)
                    .Sum(lockItem => lockItem.ZoneCells);
                if ((zoneCellsAtNextSpin + lockCandidate.ZoneCells) > (C.Cols - 1))
                {
                    return null;
                }

                if (!IsEdfFeasible(existingLocks, lockCandidate))
                {
                    return null;
                }

                return new ResolvedFeature
                {
                    FeatureId = FeatureIds.Wheel,
                    FeatureSymId = 11,
                    SpinNumber = spin,
                    Column = col,
                    TargetSymbolId = targetSymbolId,
                    StackMultiplier = C.WheelStackMultiplier,
                    Depth = depth,
                };

            default:
                throw new InvalidOperationException($"Unknown feature '{featureId}'.");
        }
    }

    private IEnumerable<int> OrderWheelCandidates(MathInput input, SymbolContext symbols)
    {
        var ordered = new List<int>();

        if (input.WheelSymbolOrder is not null)
        {
            foreach (var name in input.WheelSymbolOrder)
            {
                var symId = _symbolTable.GetByName(name).Id;
                if (symbols.TargetById.ContainsKey(symId) && !ordered.Contains(symId) && !symbols.PrizeUpgradeTargetIds.Contains(symId))
                {
                    ordered.Add(symId);
                }
            }
        }

        ordered.AddRange(
            symbols.TargetById
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key)
                .Select(kvp => kvp.Key)
                .Where(symId => !ordered.Contains(symId) && !symbols.PrizeUpgradeTargetIds.Contains(symId)));

        return ordered;
    }

    private int? PickWheelTarget(MathInput input, SymbolContext symbols, IReadOnlyList<ResolvedFeature> placed)
    {
        var used = placed
            .Where(feature => feature.FeatureId == FeatureIds.Wheel && feature.TargetSymbolId.HasValue)
            .Select(feature => feature.TargetSymbolId!.Value)
            .ToHashSet();

        foreach (var symId in OrderWheelCandidates(input, symbols))
        {
            if (!used.Contains(symId))
            {
                return symId;
            }
        }

        return null;
    }

    private bool IsEdfFeasible(IEnumerable<WheelLock> existingLocks, WheelLock newLock)
    {
        var tasks = existingLocks
            .Select(lockItem => (Demand: lockItem.PreWins, Deadline: lockItem.WheelSpin - 1))
            .Append((Demand: newLock.PreWins, Deadline: newLock.WheelSpin - 1))
            .Where(task => task.Demand > 0)
            .OrderBy(task => task.Deadline)
            .ToList();

        if (tasks.Count == 0)
        {
            return true;
        }

        var wheelSpins = existingLocks
            .Select(lockItem => lockItem.WheelSpin)
            .Append(newLock.WheelSpin)
            .ToHashSet();
        var cap = 0;
        var demand = 0;
        var taskIndex = 0;
        var maxDeadline = tasks.Max(task => task.Deadline);

        for (var deadline = 1; deadline <= maxDeadline; deadline++)
        {
            cap += C.Cols * (wheelSpins.Contains(deadline) ? C.MinPush : C.MaxPush);

            while (taskIndex < tasks.Count && tasks[taskIndex].Deadline <= deadline)
            {
                demand += tasks[taskIndex].Demand;
                taskIndex++;
            }

            if (demand > cap)
            {
                return false;
            }
        }

        return true;
    }

    private List<WheelLock> BuildWheelLocks(
        SymbolContext symbols,
        IEnumerable<ResolvedFeature> placedFeatures,
        List<string> planLog)
    {
        var wheelLocks = placedFeatures
            .Where(feature => feature.FeatureId == FeatureIds.Wheel && feature.TargetSymbolId.HasValue)
            .Select(feature => BuildWheelLock(feature.TargetSymbolId!.Value, feature.SpinNumber, symbols.TargetById[feature.TargetSymbolId.Value]))
            .OrderBy(lockItem => lockItem.WheelSpin)
            .ToList();

        foreach (var wheelLock in wheelLocks)
        {
            planLog.Add(
                $"WHEEL lock sym={_symbolTable.GetById(wheelLock.SymbolId).Name} spin={wheelLock.WheelSpin} stack={wheelLock.StackMultiplier} pre={wheelLock.PreWins} post={wheelLock.PostWins} zone={wheelLock.ZoneCells}");
        }

        return wheelLocks;
    }

    private static WheelLock BuildWheelLock(int symbolId, int wheelSpin, int target)
    {
        var zoneCells = Math.Min(target / C.WheelStackMultiplier, C.Cols - 1);
        var postWins = zoneCells * C.WheelStackMultiplier;
        return new WheelLock
        {
            SymbolId = symbolId,
            WheelSpin = wheelSpin,
            StackMultiplier = C.WheelStackMultiplier,
            ZoneCells = zoneCells,
            PreWins = Math.Max(0, target - postWins),
            PostWins = postWins,
        };
    }
}
