using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoinPusherEngine;

public static class TicketSerializer
{
    public static string Serialize(GameMasterPlan plan)
    {
        var payload = new TicketDto
        {
            WinInfo = new WinInfoDto
            {
                TotalSpins = plan.TotalSpins,
                PrizeUpgradeMap = plan.PrizeUpgradeMap.Count == 0
                    ? null
                    : plan.PrizeUpgradeMap.ToDictionary(
                        kvp => kvp.Key.ToString(),
                        kvp => kvp.Value),
                WinSymbols = plan.TargetCollection
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => new WinSymbolDto { Id = kvp.Key, Target = kvp.Value })
                    .ToList(),
            },
            StartingBoard = SerializeBoard(plan.SpinPlans[0].BoardAtStart),
            Turns = plan.SpinPlans.Select(SerializeTurn).ToList(),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        });
    }

    private static List<List<CellDto>> SerializeBoard(CellState?[,] board)
    {
        var rows = new List<List<CellDto>>();
        for (var row = 0; row < C.Rows; row++)
        {
            var currentRow = new List<CellDto>();
            for (var col = 0; col < C.Cols; col++)
            {
                var cell = board[row, col] ?? CellState.Normal(0);
                currentRow.Add(new CellDto { Id = cell.SymId });
            }

            rows.Add(currentRow);
        }

        return rows;
    }

    private static TurnDto SerializeTurn(SpinPlan spin)
    {
        return new TurnDto
        {
            IsExtraSpin = spin.IsExtraSpin ? true : null,
            ParentTurnIndex = spin.ParentTurnIndex,
            Pushers = spin.Pushers
                .Select(pusher => new PusherDto
                {
                    PushValue = pusher.PushValue,
                    FeatureId = pusher.FeatureId,
                })
                .ToList(),
            Spawns = spin.PlannedSpawns
                .OrderBy(kvp => kvp.Key.ToFlatIndex())
                .Select(kvp => SerializeSpawn(kvp.Key, kvp.Value))
                .ToList(),
        };
    }

    private static SpawnDto SerializeSpawn(BoardPosition position, CellState cell)
    {
        return new SpawnDto
        {
            Pos = position.ToFlatIndex(),
            Id = cell.SymId,
            Feature = cell.IsFeature
                ? SerializeFeature(cell)
                : null,
        };
    }

    private static FeatureDto SerializeFeature(CellState cell)
    {
        return new FeatureDto
        {
            FeatureId = cell.SymId,
            ConvertToId = cell.ConvertSymId,
            WheelSymbolId = cell.FParams?.WheelSymId,
            WheelStackMultiplier = cell.FParams?.WheelStackMultiplier,
            FromSymId = cell.FParams?.FromSymId,
            ToSymId = cell.FParams?.ToSymId,
            GrantedTurnIndex = cell.FParams?.GrantedTurnIndex,
            Depth = cell.FParams?.Depth ?? 0,
            Children = [],
        };
    }

    private sealed class TicketDto
    {
        public required WinInfoDto WinInfo { get; init; }
        public required List<List<CellDto>> StartingBoard { get; init; }
        public required List<TurnDto> Turns { get; init; }
    }

    private sealed class WinInfoDto
    {
        public int TotalSpins { get; init; }
        public Dictionary<string, int>? PrizeUpgradeMap { get; init; }
        public required List<WinSymbolDto> WinSymbols { get; init; }
    }

    private sealed class WinSymbolDto
    {
        public int Id { get; init; }
        public int Target { get; init; }
    }

    private sealed class CellDto
    {
        public int Id { get; init; }
    }

    private sealed class TurnDto
    {
        public bool? IsExtraSpin { get; init; }
        public int? ParentTurnIndex { get; init; }
        public required List<PusherDto> Pushers { get; init; }
        public required List<SpawnDto> Spawns { get; init; }
    }

    private sealed class PusherDto
    {
        public int PushValue { get; init; }
        public int? FeatureId { get; init; }
    }

    private sealed class SpawnDto
    {
        public int Pos { get; init; }
        public int Id { get; init; }
        public FeatureDto? Feature { get; init; }
    }

    private sealed class FeatureDto
    {
        public int FeatureId { get; init; }
        public int ConvertToId { get; init; }
        public int? WheelSymbolId { get; init; }
        public int? WheelStackMultiplier { get; init; }
        public int? FromSymId { get; init; }
        public int? ToSymId { get; init; }
        public int? GrantedTurnIndex { get; init; }
        public int Depth { get; init; }
        public required List<FeatureDto> Children { get; init; }
    }
}
