using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Services;
using PokerSettle.Core.Stats;

namespace PokerSettle.Core.Leaderboard;

/// <summary>Sends the payload to the leaderboard web app; returns the response body.</summary>
public interface ILeaderboardTransport
{
    Task<string> PostAsync(string url, string json, CancellationToken cancellationToken);
}

/// <summary>Plain HTTPS POST. Google Apps Script answers with a redirect, which HttpClient follows.</summary>
public sealed class HttpLeaderboardTransport : ILeaderboardTransport, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<string> PostAsync(string url, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}

public enum LeaderboardPublishStatus
{
    Published,
    NotConfigured,
    DemoDatabase,
    Failed,
}

public sealed record LeaderboardPublishResult(LeaderboardPublishStatus Status, int Players = 0, string? Error = null);

/// <summary>
/// Publishes the all-time board (active players) to a Google Sheet through an Apps Script web app
/// (scripts/leaderboard-apps-script.gs). Only names and results leave the device, never phone
/// numbers or UPI IDs. When auto-publish is on, it re-publishes after results or players change.
/// </summary>
public sealed class LeaderboardPublisher
{
    private readonly DataStore _data;
    private readonly AppSettings _settings;
    private readonly ILeaderboardTransport _transport;
    private readonly SnackbarService _snackbar;
    private readonly IClock _clock;
    private readonly ILogger<LeaderboardPublisher> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LeaderboardPublisher(DataStore data, AppSettings settings, ILeaderboardTransport transport, SnackbarService snackbar,
        IMessenger messenger, IClock clock, ILogger<LeaderboardPublisher> log)
    {
        _data = data;
        _settings = settings;
        _transport = transport;
        _snackbar = snackbar;
        _clock = clock;
        _log = log;
        messenger.Register<LeaderboardPublisher, DataChangedMessage>(this, static (r, m) =>
        {
            if (m.Affects(DataArea.Results | DataArea.Players)) _ = r.AutoPublishAsync();
        });
    }

    public bool IsConfigured =>
        Uri.TryCreate(_settings.LeaderboardUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(_settings.LeaderboardSecret);

    /// <summary>The auto-publish of the most recent change (lets tests wait for it).</summary>
    public Task<LeaderboardPublishResult>? LastAutoPublish { get; private set; }

    public async Task<LeaderboardPublishResult> PublishAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return new(LeaderboardPublishStatus.NotConfigured);
        if (_data.IsDemo) return new(LeaderboardPublishStatus.DemoDatabase);

        // One publish at a time, so the sheet always ends with the latest board.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var watch = Stopwatch.StartNew();
            var rows = BoardMath.RankByNet(await _data.GetBoardAsync(TimeFilters.Resolve(TimeFilterKind.AllTime, _clock.Today, null, null), false));
            var json = JsonSerializer.Serialize(BuildPayload(rows), LeaderboardJsonContext.Default.LeaderboardPayload);
            string body;
            try
            {
                body = await _transport.PostAsync(_settings.LeaderboardUrl!, json, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                var (reason, error) = (ex as HttpRequestException)?.StatusCode switch
                {
                    // Google's "You need access" page: the deployment isn't public.
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ("access denied",
                        "Google refused access. In Apps Script, edit the deployment and set \"Who has access\" to \"Anyone\"."),
                    HttpStatusCode.NotFound => ("not found", "There's no web app at this URL. Copy the Web app URL (ending in /exec) again."),
                    not null => ("http error", "The leaderboard web app answered with an error. Try again later."),
                    null => (ex.GetType().Name, "Couldn't reach the leaderboard. Check your internet connection and the web app URL."),
                };
                _log.LeaderboardPublishFailed(reason);
                return new(LeaderboardPublishStatus.Failed, Error: error);
            }

            var reply = ParseReply(body);
            if (reply?.Ok != true)
            {
                var error = reply is null
                    ? "The web app URL didn't answer as expected. Check it ends in /exec and is deployed with access \"Anyone\"."
                    : reply.Error == "wrong secret" ? "The secret doesn't match the one in the Apps Script." : $"The leaderboard script said: {reply.Error}";
                _log.LeaderboardPublishFailed(reply is null ? "unexpected reply" : reply.Error ?? "not ok");
                return new(LeaderboardPublishStatus.Failed, Error: error);
            }

            _settings.LeaderboardLastPublishedAt = _clock.Now;
            _log.LeaderboardPublished(rows.Count, watch.ElapsedMilliseconds);
            return new(LeaderboardPublishStatus.Published, rows.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task AutoPublishAsync()
    {
        if (!_settings.LeaderboardAutoPublish || !IsConfigured || _data.IsDemo) return Task.CompletedTask;
        var task = RunAutoAsync();
        LastAutoPublish = task;
        return task;

        async Task<LeaderboardPublishResult> RunAutoAsync()
        {
            try
            {
                var result = await PublishAsync();
                if (result.Status == LeaderboardPublishStatus.Failed)
                {
                    _snackbar.Show("Couldn't update the online leaderboard. Try Publish now in Settings.");
                }
                return result;
            }
            catch (Exception ex)
            {
                _log.CommandFailed(ex, "LeaderboardPublisher.Auto");
                return new(LeaderboardPublishStatus.Failed, Error: ex.Message);
            }
        }
    }

    private LeaderboardPayload BuildPayload(IReadOnlyList<RankedBoardRow> rows)
    {
        var money = _settings.Formatter;
        return new LeaderboardPayload
        {
            Secret = _settings.LeaderboardSecret!.Trim(),
            UpdatedAt = _clock.Now.ToString("O", CultureInfo.InvariantCulture),
            UpdatedText = money.FormatDateTime(_clock.Now),
            Currency = _settings.CurrencySymbol,
            Rows = [.. rows.Select(r => new LeaderboardRow
            {
                Rank = r.Rank,
                Name = r.Row.Name,
                Games = r.Row.Games,
                Net = Major(r.Row.Net),
                Won = Major(r.Row.Won),
                Lost = Major(r.Row.Lost),
                WinRate = Math.Round(r.Row.WinRate, 4),
                Average = Major(r.Row.Average),
                BiggestWin = r.Row.BiggestWin is { } best ? Major(best) : null,
                BiggestLoss = r.Row.BiggestLoss is { } worst ? Major(worst) : null,
                LastPlayed = r.Row.LastPlayed?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            })],
        };

        static decimal Major(long paise) => paise / 100m;
    }

    private static LeaderboardReply? ParseReply(string body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, LeaderboardJsonContext.Default.LeaderboardReply);
        }
        catch (JsonException)
        {
            // An HTML page (sign-in, "script not found") instead of the script's JSON.
            return null;
        }
    }
}

/// <summary>What the Apps Script receives. Amounts are in major units (rupees) so the sheet can sort and sum them.</summary>
public sealed class LeaderboardPayload
{
    public required string Secret { get; init; }
    public required string UpdatedAt { get; init; }
    public required string UpdatedText { get; init; }
    public required string Currency { get; init; }
    public required List<LeaderboardRow> Rows { get; init; }
}

public sealed class LeaderboardRow
{
    public int Rank { get; init; }
    public required string Name { get; init; }
    public int Games { get; init; }
    public decimal Net { get; init; }
    public decimal Won { get; init; }
    public decimal Lost { get; init; }
    public double WinRate { get; init; }
    public decimal Average { get; init; }
    public decimal? BiggestWin { get; init; }
    public decimal? BiggestLoss { get; init; }
    public string? LastPlayed { get; init; }
}

public sealed class LeaderboardReply
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LeaderboardPayload))]
[JsonSerializable(typeof(LeaderboardReply))]
internal sealed partial class LeaderboardJsonContext : JsonSerializerContext
{
}
