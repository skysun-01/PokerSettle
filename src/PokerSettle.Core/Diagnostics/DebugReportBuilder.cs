using System.Globalization;
using System.Text;
using System.Text.Json;
using PokerSettle.Core.Data;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Services;

namespace PokerSettle.Core.Diagnostics;

/// <summary>
/// "Copy debug report": versions, device, database size, settings, data counts and the last 200 log
/// entries, as plain text ready to paste into an issue or a Claude Code session. No phone numbers or UPI IDs.
/// </summary>
public sealed class DebugReportBuilder(DataStore data, AppSettings settings, IAppEnvironment environment, JsonLogWriter writer, IClock clock)
{
    public const int LogEntries = 200;

    public async Task<string> BuildAsync()
    {
        await writer.FlushAsync();
        var info = await data.GetInfoAsync();
        var records = await Task.Run(() => writer.Store.ReadAll());

        var sb = new StringBuilder();
        sb.AppendLine("# Poker Settle debug report");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Generated: {clock.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"App version: {environment.AppVersion} (build {environment.BuildNumber}){(environment.IsDebugBuild ? " DEBUG" : "")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Schema version: {info.SchemaVersion} (app supports {data.SchemaVersion})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Platform: {environment.Platform}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Device: {environment.DeviceDescription}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OS: {environment.OsDescription}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Database: {info.Path} ({info.SizeBytes:N0} bytes){(data.IsDemo ? " [DEMO]" : "")}");
        if (data.Recovery is { } recovery)
            sb.AppendLine(CultureInfo.InvariantCulture, $"Recovered at startup: {recovery.Reason} → {recovery.MovedTo}");
        sb.AppendLine();
        sb.AppendLine("## Settings");
        foreach (var (key, value) in settings.Describe()) sb.AppendLine(CultureInfo.InvariantCulture, $"{key}: {value}");
        sb.AppendLine();
        sb.AppendLine("## Data");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Players: {info.Players} active, {info.ArchivedPlayers} archived");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Games: {info.Games} ({info.GamesInProgress} in progress)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Entries: {info.Entries}, buy-ins: {info.BuyIns}, payments: {info.Transfers} ({info.UnpaidTransfers} unpaid)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Snapshots kept: {info.Snapshots}");
        sb.AppendLine();
        var last = records.Skip(Math.Max(0, records.Count - LogEntries)).ToList();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Last {last.Count} log entries (JSON lines, oldest first)");
        if (last.Count == 0) sb.AppendLine(settings.DebugLogging ? "(none yet)" : "(debug logging is off)");
        foreach (var record in last) sb.AppendLine(JsonSerializer.Serialize(record, LogJsonContext.Lines.LogRecord));
        return sb.ToString();
    }
}
