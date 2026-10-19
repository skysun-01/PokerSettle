using System.Text;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;

namespace PokerSettle.Core.Settlement;

/// <summary>
/// The plain-text summary for "Share" (WhatsApp etc.) and "Copy": results, payments with the payee's
/// UPI ID when saved, and the pot.
/// </summary>
public static class ShareText
{
    public static string Build(GameSnapshot game, MoneyFormatter formatter)
    {
        var money = new GameMoney(formatter, game.Game.Settings);
        var sb = new StringBuilder();
        sb.Append("♠ ").Append(game.Game.Title);
        var date = formatter.FormatDate(game.Game.Date);
        if (!game.Game.Title.Contains(formatter.FormatShortDate(game.Game.Date), StringComparison.OrdinalIgnoreCase))
            sb.Append(" · ").Append(date);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(game.Game.Location)) sb.AppendLine(game.Game.Location);
        sb.AppendLine();

        sb.AppendLine("Results");
        var results = game.Entries
            .Select(e => (e.Player, Net: e.Entry.Net ?? 0))
            .OrderByDescending(r => r.Net)
            .ThenBy(r => r.Player.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (player, net) in results) sb.Append("• ").Append(player.Name).Append(": ").AppendLine(formatter.FormatSigned(net));

        if (game.Adjustments.Count > 0)
        {
            var total = game.Adjustments.Sum(a => a.Amount);
            var how = game.Adjustments.All(a => a.Kind == AdjustmentKind.SplitDifference) ? "split equally" : "assigned to one player";
            sb.Append("(Includes a ").Append(money.Units(Math.Abs(total))).Append(" count difference, ").Append(how).AppendLine(")");
        }

        sb.AppendLine();
        if (game.Transfers.Count == 0)
        {
            sb.AppendLine("Everyone is even. No payments needed.");
        }
        else
        {
            sb.AppendLine("Payments");
            foreach (var t in game.Transfers.OrderBy(t => t.IsPaid).ThenByDescending(t => t.Amount))
            {
                var from = game.FindPlayer(t.FromId);
                var to = game.FindPlayer(t.ToId);
                sb.Append("• ").Append(from?.Name ?? "?").Append(" pays ").Append(to?.Name ?? "?").Append(' ').Append(formatter.Format(t.Amount));
                if (!string.IsNullOrWhiteSpace(to?.UpiId)) sb.Append(" (UPI: ").Append(to.UpiId).Append(')');
                if (t.IsPaid) sb.Append(" ✓ paid");
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.Append("Pot ").Append(money.UnitsWithMoney(game.Pot)).Append(" · ").Append(game.Entries.Count).AppendLine(" players");
        // No invisible word joiners in text people paste into chats or UPI apps.
        return sb.ToString().Replace(MoneyFormatter.Joiner.ToString(), string.Empty, StringComparison.Ordinal).TrimEnd();
    }
}
