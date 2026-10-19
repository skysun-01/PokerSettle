using PokerSettle.Core.Models;
using PokerSettle.Core.Players;
using PokerSettle.Core.Settlement;

namespace PokerSettle.Core.Data;

/// <summary>
/// Deterministic fake history (players, finalized games, payments) for the Debug-only demo
/// database and for the performance test.
/// </summary>
public static class DemoData
{
    private static readonly string[] FirstNames =
    [
        "Aarav", "Vivaan", "Aditya", "Vihaan", "Arjun", "Sai", "Reyansh", "Krishna", "Ishaan", "Rohan",
        "Ananya", "Diya", "Priya", "Saanvi", "Aadhya", "Kavya", "Meera", "Riya", "Neha", "Pooja",
        "Rahul", "Amit", "Vikram", "Karan", "Siddharth", "Nikhil", "Manish", "Deepak", "Suresh", "Varun",
        "Sneha", "Shruti", "Tanvi", "Nisha", "Ishita", "Aditi", "Sana", "Zoya", "Farhan", "Kabir",
        "Dev", "Yash", "Om", "Jay", "Aryan", "Lakshmi", "Gauri", "Tara", "Esha", "Mira",
    ];

    private static readonly string[] LastNames =
    [
        "Sharma", "Verma", "Iyer", "Nair", "Reddy", "Gupta", "Mehta", "Patel", "Shah", "Rao",
        "Kapoor", "Joshi", "Menon", "Pillai", "Bose", "Das", "Khan", "Singh", "Chopra", "Malhotra",
    ];

    private static readonly string[] Venues = ["Rahul's place", "The terrace", "Club house", "Amit's flat", "Office lounge"];

    public static (List<Player> Players, List<GameRecord> Games) Generate(int playerCount, int gameCount, int seed, DateOnly lastDate)
    {
        var rng = new Random(seed);
        var start = lastDate.AddDays(-gameCount);
        var created = new DateTimeOffset(start.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero);

        var players = new List<Player>(playerCount);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < playerCount; i++)
        {
            string name;
            do
            {
                name = $"{FirstNames[rng.Next(FirstNames.Length)]} {LastNames[rng.Next(LastNames.Length)]}";
            }
            while (!usedNames.Add(name));
            players.Add(new Player
            {
                Id = i + 1,
                Name = name,
                Nickname = rng.Next(5) == 0 ? name.Split(' ')[0][..3] : null,
                Color = PlayerNames.ColorFor(name),
                IsArchived = i >= playerCount - 2,
                CreatedAt = created,
            });
        }

        // Regulars play far more often than occasional guests.
        var weights = players.Select((_, i) => i < 12 ? 6.0 : i < 25 ? 2.0 : 0.6).ToArray();

        var games = new List<GameRecord>(gameCount);
        long buyInId = 1, transferId = 1;
        for (var g = 0; g < gameCount; g++)
        {
            var gameId = g + 1;
            var date = start.AddDays(g + 1);
            var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(20, 0)), TimeSpan.Zero);
            var defaultBuyIn = rng.Next(4) == 0 ? 100_000L : 50_000L;
            var seats = PickPlayers(rng, weights, rng.Next(4, 11), includeArchived: g < gameCount / 2);

            var entries = new List<Entry>();
            var buyIns = new List<BuyIn>();
            var lines = new List<PlayerLine>();
            long pot = 0;
            for (var s = 0; s < seats.Count; s++)
            {
                var playerId = players[seats[s]].Id;
                var count = 1;
                while (count < 6 && rng.NextDouble() < 0.35) count++;
                for (var k = 0; k < count; k++)
                {
                    buyIns.Add(new BuyIn(buyInId++, gameId, playerId, defaultBuyIn, at.AddMinutes(k * 40 + s)));
                }
                pot += count * defaultBuyIn;
                lines.Add(new PlayerLine(playerId, s, count * defaultBuyIn, count, null));
            }

            // Split the pot into cash-outs in ₹10 steps.
            var shares = seats.Select(_ => Math.Pow(rng.NextDouble(), 2) + 0.05).ToArray();
            var total = shares.Sum();
            var cashOuts = new long[seats.Count];
            long assigned = 0;
            for (var s = 0; s < seats.Count - 1; s++)
            {
                cashOuts[s] = (long)(pot * shares[s] / total / 1_000) * 1_000;
                assigned += cashOuts[s];
            }
            cashOuts[^1] = pot - assigned;

            for (var s = 0; s < seats.Count; s++)
            {
                lines[s] = lines[s] with { CashOut = cashOuts[s] };
                entries.Add(new Entry { GameId = gameId, PlayerId = lines[s].PlayerId, Position = s, CashOut = cashOuts[s] });
            }

            var result = SettlementEngine.Settle(lines, new SettlementOptions(false, null, SettlementMode.FewestPayments, null, RoundingMode.None));
            var paid = g < gameCount - 6 || rng.Next(3) == 0;
            var transfers = result.Payments
                .Select(p => new Transfer(transferId++, gameId, p.FromId, p.ToId, p.Amount, paid, paid ? at.AddDays(1) : null))
                .ToList();
            var netById = result.Results.ToDictionary(r => r.PlayerId, r => r.Net);
            entries = entries.Select(e => e with { Net = netById[e.PlayerId] }).ToList();

            games.Add(new GameRecord(
                new Game
                {
                    Id = gameId,
                    Title = date.ToString("ddd, d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-IN")),
                    Date = date,
                    Location = rng.Next(3) == 0 ? Venues[rng.Next(Venues.Length)] : null,
                    Status = GameStatus.Finalized,
                    Settings = new GameSettings { DefaultBuyIn = defaultBuyIn, StartWithBuyIn = true },
                    CreatedAt = at.AddHours(-1),
                    UpdatedAt = at.AddHours(4),
                    FinalizedAt = at.AddHours(4),
                },
                entries, buyIns, [], transfers));
        }
        return (players, games);
    }

    private static List<int> PickPlayers(Random rng, double[] weights, int count, bool includeArchived)
    {
        var chosen = new List<int>(count);
        var available = Enumerable.Range(0, weights.Length).Where(i => includeArchived || i < weights.Length - 2).ToList();
        count = Math.Min(count, available.Count);
        while (chosen.Count < count)
        {
            var total = available.Sum(i => weights[i]);
            var pick = rng.NextDouble() * total;
            foreach (var i in available)
            {
                pick -= weights[i];
                if (pick > 0) continue;
                chosen.Add(i);
                available.Remove(i);
                break;
            }
        }
        return chosen;
    }
}
