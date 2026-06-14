using CoinPusherEngine;

var symbolTable = new SymbolTable();
var engine = new CoinPusherEngineService(symbolTable);

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

var plan = engine.GeneratePlan(input);
var trace = GameEngine.ExecuteWithTrace(plan);

Console.WriteLine("CoinPusherEngine console runner");
Console.WriteLine(new string('=', 80));
Console.WriteLine($"Seed: {plan.Seed}");
Console.WriteLine($"Total spins: {plan.TotalSpins}");
Console.WriteLine(
    $"Targets: {string.Join(", ", plan.TargetCollection.Select(kvp => $"{symbolTable.GetById(kvp.Key).Name}={kvp.Value}"))}");
Console.WriteLine(
    $"Wheel locks: {(plan.WheelLocks.Count == 0 ? "none" : string.Join(", ", plan.WheelLocks.Select(lockInfo => $"{symbolTable.GetById(lockInfo.SymbolId).Name}@spin{lockInfo.WheelSpin} pre={lockInfo.PreWins} post={lockInfo.PostWins}")))}");
Console.WriteLine();

Console.WriteLine("Starting board");
Console.WriteLine(BoardFormatter.FormatBoard(plan.SpinPlans[0].BoardAtStart, symbolTable));
Console.WriteLine();

foreach (var spinTrace in trace.SpinTraces)
{
    var spinPlan = plan.SpinPlans[spinTrace.SpinNumber - 1];

    Console.WriteLine(new string('-', 80));
    Console.WriteLine(
        spinTrace.IsExtraSpin
            ? $"Spin {spinTrace.SpinNumber} (extra spin, parent turn index {spinTrace.ParentTurnIndex})"
            : $"Spin {spinTrace.SpinNumber}");
    Console.WriteLine($"Pushers: {BoardFormatter.FormatPushers(spinPlan.Pushers)}");
    Console.WriteLine($"Planned spawns: {BoardFormatter.FormatSpawns(spinPlan.PlannedSpawns, symbolTable)}");
    Console.WriteLine();

    PrintBoardStage("Board at start", spinTrace.BoardAtStart, symbolTable);
    PrintBoardStage("After flatten stale features", spinTrace.BoardAfterFlatten, symbolTable);
    PrintBoardStage("After push collection", spinTrace.BoardAfterPush, symbolTable);
    PrintBoardStage("After clockwise rotation", spinTrace.BoardAfterRotate, symbolTable);
    PrintBoardStage("After spawns", spinTrace.BoardAfterSpawns, symbolTable);
    PrintBoardStage("After feature resolution", spinTrace.BoardAfterFeatures, symbolTable);

    Console.WriteLine($"Running totals: {BoardFormatter.FormatTotals(spinTrace.TotalsAfterSpin, symbolTable)}");
    Console.WriteLine();
}

Console.WriteLine(new string('=', 80));
Console.WriteLine($"Final totals: {BoardFormatter.FormatTotals(trace.Result.Totals, symbolTable)}");
Console.WriteLine("Ticket JSON preview:");
Console.WriteLine(TicketSerializer.Serialize(plan));

static void PrintBoardStage(string title, CellState?[,] board, SymbolTable symbolTable)
{
    Console.WriteLine(title);
    Console.WriteLine(BoardFormatter.FormatBoard(board, symbolTable));
    Console.WriteLine();
}
