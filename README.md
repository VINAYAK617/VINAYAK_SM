# CoinPusherEngine

Predetermined-outcome coin-pusher engine implemented in C# from the provided design document.

## Projects

- `src/CoinPusherEngine` - engine library
- `tests/CoinPusherEngine.Tests` - focused verification tests

## Implemented pieces

- 5x5 board model with push, rotation, and zone collection
- planner pipeline for:
  - input validation
  - feature count selection
  - feature placement
  - win scheduling
  - backward board construction
  - spawn resolution
  - full forward verification
- runtime replay engine
- ticket JSON serializer
- core features:
  - WHEEL
  - FLUSH
  - EXTRA_SPIN
  - PRIZE_UPGRADE

## Quick usage

```csharp
using CoinPusherEngine;

var engine = new CoinPusherEngineService();

var input = new MathInput
{
    Seed = 42,
    TotalBaseSpins = 4,
    TargetCollection = new Dictionary<string, int>
    {
        ["DIAMOND"] = 5,
        ["GOLD"] = 4,
        ["RUBY"] = 3,
    },
};

GameMasterPlan plan = engine.GeneratePlan(input);
string ticketJson = TicketSerializer.Serialize(plan);
```

## Validation

```bash
dotnet test CoinPusherEngine.sln
```