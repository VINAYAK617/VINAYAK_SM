using System.Text;

namespace CoinPusherEngine;

public static class BoardFormatter
{
    public static string FormatBoard(CellState?[,] board, SymbolTable symbolTable)
    {
        var builder = new StringBuilder();
        builder.AppendLine("      C0       C1       C2       C3       C4");
        builder.AppendLine("   +--------+--------+--------+--------+--------+");

        for (var row = 0; row < C.Rows; row++)
        {
            builder.Append($"R{row} |");
            for (var col = 0; col < C.Cols; col++)
            {
                builder.Append($" {FormatCell(board[row, col], symbolTable),-6} |");
            }

            builder.AppendLine();
            builder.AppendLine("   +--------+--------+--------+--------+--------+");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatPushers(IReadOnlyList<PusherPlan> pushers)
    {
        return string.Join(
            " | ",
            pushers.Select((pusher, index) =>
                pusher.IsFeature
                    ? $"C{index}: FLUSH"
                    : $"C{index}: push {pusher.PushValue}"));
    }

    public static string FormatSpawns(
        IReadOnlyDictionary<BoardPosition, CellState> spawns,
        SymbolTable symbolTable)
    {
        if (spawns.Count == 0)
        {
            return "none";
        }

        return string.Join(
            ", ",
            spawns
                .OrderBy(kvp => kvp.Key.Row)
                .ThenBy(kvp => kvp.Key.Col)
                .Select(kvp =>
                    $"({kvp.Key.Row},{kvp.Key.Col})={FormatCell(kvp.Value, symbolTable)}"));
    }

    public static string FormatTotals(
        IReadOnlyDictionary<int, int> totals,
        SymbolTable symbolTable)
    {
        if (totals.Count == 0)
        {
            return "none";
        }

        return string.Join(
            ", ",
            totals
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{ShortName(symbolTable.GetById(kvp.Key).Name)}={kvp.Value}"));
    }

    private static string FormatCell(CellState? cell, SymbolTable symbolTable)
    {
        if (cell is null)
        {
            return ".";
        }

        if (cell.IsFeature)
        {
            var prefix = cell.FeatureId switch
            {
                FeatureIds.Wheel => "WHL",
                FeatureIds.ExtraSpin => "XSP",
                FeatureIds.PrizeUpgrade => "PRU",
                _ => "FEAT",
            };
            var convertTo = ShortName(symbolTable.GetById(cell.ConvertSymId).Name);
            return $"{prefix}>{convertTo}";
        }

        var baseName = ShortName(symbolTable.GetById(cell.SymId).Name);
        return cell.StackCount > 1
            ? $"{baseName}x{cell.StackCount}"
            : baseName;
    }

    private static string ShortName(string name)
    {
        return name.Length <= 3
            ? name.ToUpperInvariant()
            : name[..3].ToUpperInvariant();
    }
}
