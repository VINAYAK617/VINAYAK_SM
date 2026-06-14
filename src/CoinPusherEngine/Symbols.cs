namespace CoinPusherEngine;

public static class FeatureIds
{
    public const string Wheel = "WHEEL";
    public const string Flush = "FLUSH";
    public const string ExtraSpin = "EXTRA_SPIN";
    public const string PrizeUpgrade = "PRIZE_UPGRADE";
}

public sealed class SymbolTable
{
    private readonly IReadOnlyDictionary<int, SymbolDefinition> _byId;
    private readonly IReadOnlyDictionary<string, SymbolDefinition> _byName;

    public SymbolTable(IEnumerable<SymbolDefinition>? definitions = null)
    {
        var symbolList = (definitions ?? CreateDefaultDefinitions())
            .ToList();

        _byId = symbolList.ToDictionary(def => def.Id);
        _byName = symbolList.ToDictionary(def => def.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<SymbolDefinition> All => _byId.Values.ToList();

    public SymbolDefinition GetById(int id)
    {
        if (!_byId.TryGetValue(id, out var definition))
        {
            throw new InvalidOperationException($"Unknown symbol id {id}.");
        }

        return definition;
    }

    public SymbolDefinition GetByName(string name)
    {
        if (!_byName.TryGetValue(name, out var definition))
        {
            throw new InvalidOperationException($"Unknown symbol '{name}'.");
        }

        return definition;
    }

    public IReadOnlyList<int> GetIdsByCategory(SymbolCategory category)
    {
        return _byId.Values
            .Where(def => def.Category == category)
            .OrderBy(def => def.Id)
            .Select(def => def.Id)
            .ToList();
    }

    public static IReadOnlyDictionary<string, FeaturePlacementConfig> CreateDefaultFeatureRegistry()
    {
        return new Dictionary<string, FeaturePlacementConfig>(StringComparer.OrdinalIgnoreCase)
        {
            [FeatureIds.Wheel] = new FeaturePlacementConfig(0.40, 4, 2, 1),
            [FeatureIds.Flush] = new FeaturePlacementConfig(0.30, 5, 1, 2),
            [FeatureIds.ExtraSpin] = new FeaturePlacementConfig(0.20, 2, 1, 3),
            [FeatureIds.PrizeUpgrade] = new FeaturePlacementConfig(0.15, 1, 1, 4),
        };
    }

    public static IReadOnlyList<SymbolDefinition> CreateDefaultDefinitions()
    {
        return
        [
            new SymbolDefinition(1, "DIAMOND", "A", SymbolCategory.Win),
            new SymbolDefinition(2, "GOLD", "B", SymbolCategory.Win),
            new SymbolDefinition(3, "RUBY", "C", SymbolCategory.Win),
            new SymbolDefinition(4, "SEVEN", "D", SymbolCategory.FromSymbol, 25),
            new SymbolDefinition(5, "CROWN", "E", SymbolCategory.Filler, 20),
            new SymbolDefinition(6, "STAR", "F", SymbolCategory.Filler, 20),
            new SymbolDefinition(7, "BELL", "G", SymbolCategory.Filler, 20),
            new SymbolDefinition(8, "CHERRY", "H", SymbolCategory.Filler, 20),
            new SymbolDefinition(9, "CLOVER", "I", SymbolCategory.Filler, 20),
            new SymbolDefinition(10, "COIN", "J", SymbolCategory.Filler, 20),
            new SymbolDefinition(11, "WHEEL_SYM", "-", SymbolCategory.FeatureToken),
            new SymbolDefinition(12, "XSPIN_SYM", "-", SymbolCategory.FeatureToken),
            new SymbolDefinition(13, "FLUSH", "-", SymbolCategory.PusherFeature),
            new SymbolDefinition(14, "PRUP_SYM", "-", SymbolCategory.FeatureToken),
        ];
    }
}
