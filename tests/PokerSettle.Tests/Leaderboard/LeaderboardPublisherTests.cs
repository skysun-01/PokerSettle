using System.Net;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Leaderboard;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Services;
using PokerSettle.Core.ViewModels.Settings;
using PokerSettle.Tests.Data;

namespace PokerSettle.Tests.Leaderboard;

public class LeaderboardPublisherTests
{
    private const string Url = "https://script.google.com/macros/s/abc/exec";

    private static void Configure(Harness h, bool auto = true)
    {
        var settings = h.Get<AppSettings>();
        settings.LeaderboardUrl = Url;
        settings.LeaderboardSecret = "s3cret";
        settings.LeaderboardAutoPublish = auto;
    }

    [Fact]
    public async Task Publishes_the_ranked_all_time_board_in_rupees_without_personal_data()
    {
        using var dir = new TempDir();
        await using var h = new Harness(dir);
        Configure(h, auto: false);
        var amit = (await h.Data.AddPlayerAsync(new PlayerDraft("Amit", null, null, null, null, null))).Id;
        var rahul = (await h.Data.AddPlayerAsync(new PlayerDraft("Rahul", null, "98765 43210", "rahul@okaxis", null, null))).Id;
        await GameFlows.PlayFinalizedAsync(h.Data, h.Clock, new DateOnly(2026, 10, 1), amit, rahul, 12_550);

        var result = await h.Get<LeaderboardPublisher>().PublishAsync();

        Assert.Equal(LeaderboardPublishStatus.Published, result.Status);
        Assert.Equal(2, result.Players);
        var (url, json) = Assert.Single(h.LeaderboardTransport.Posts);
        Assert.Equal(Url, url);
        Assert.DoesNotContain("98765", json);
        Assert.DoesNotContain("okaxis", json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("s3cret", root.GetProperty("secret").GetString());
        Assert.Equal("₹", root.GetProperty("currency").GetString());
        var rows = root.GetProperty("rows");
        Assert.Equal("Amit", rows[0].GetProperty("name").GetString());
        Assert.Equal(1, rows[0].GetProperty("rank").GetInt32());
        Assert.Equal(125.5m, rows[0].GetProperty("net").GetDecimal());
        Assert.Equal(-125.5m, rows[1].GetProperty("net").GetDecimal());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("biggestWin").ValueKind);
        Assert.Equal("2026-10-01", rows[1].GetProperty("lastPlayed").GetString());
        Assert.NotNull(h.Get<AppSettings>().LeaderboardLastPublishedAt);
    }

    [Fact]
    public async Task Does_nothing_until_url_and_secret_are_set()
    {
        using var dir = new TempDir();
        await using var h = new Harness(dir);
        var settings = h.Get<AppSettings>();
        settings.LeaderboardUrl = "http://not-https.example/exec";
        settings.LeaderboardSecret = "s3cret";

        var result = await h.Get<LeaderboardPublisher>().PublishAsync();

        Assert.Equal(LeaderboardPublishStatus.NotConfigured, result.Status);
        Assert.Empty(h.LeaderboardTransport.Posts);
    }

    [Theory]
    [InlineData("""{"ok":false,"error":"wrong secret"}""", "secret doesn't match")]
    [InlineData("<html>Sign in</html>", "didn't answer as expected")]
    public async Task Reports_a_wrong_secret_or_a_bad_deployment(string reply, string expected)
    {
        using var dir = new TempDir();
        await using var h = new Harness(dir);
        Configure(h, auto: false);
        h.LeaderboardTransport.Respond = _ => reply;

        var result = await h.Get<LeaderboardPublisher>().PublishAsync();

        Assert.Equal(LeaderboardPublishStatus.Failed, result.Status);
        Assert.Contains(expected, result.Error);
        Assert.Null(h.Get<AppSettings>().LeaderboardLastPublishedAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "\"Who has access\" to \"Anyone\"")]
    [InlineData(HttpStatusCode.NotFound, "no web app at this URL")]
    public async Task Explains_what_to_fix_when_google_refuses_the_request(HttpStatusCode status, string expected)
    {
        using var dir = new TempDir();
        await using var h = new Harness(dir);
        Configure(h, auto: false);
        h.LeaderboardTransport.Respond = _ => throw new HttpRequestException("refused", null, status);

        var result = await h.Get<LeaderboardPublisher>().PublishAsync();

        Assert.Equal(LeaderboardPublishStatus.Failed, result.Status);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public async Task Offline_publish_fails_with_a_message_instead_of_throwing()
    {
        using var dir = new TempDir();
        await using var h = new Harness(dir);
        Configure(h, auto: false);
        h.LeaderboardTransport.Respond = _ => throw new HttpRequestException("no network");

        var result = await h.Get<LeaderboardPublisher>().PublishAsync();

        Assert.Equal(LeaderboardPublishStatus.Failed, result.Status);
        Assert.Contains("internet connection", result.Error);
    }

    [Fact]
    public async Task Auto_publishes_when_results_change_and_only_when_enabled()
    {
        using var dir = new TempDir();
        await using var h = new Harness(dir);
        var publisher = h.Get<LeaderboardPublisher>();
        Configure(h, auto: false);

        h.Messenger.Send(new DataChangedMessage(DataArea.Results));
        Assert.Null(publisher.LastAutoPublish);

        h.Get<AppSettings>().LeaderboardAutoPublish = true;
        h.Messenger.Send(new DataChangedMessage(DataArea.Payments));
        Assert.Null(publisher.LastAutoPublish);

        h.Messenger.Send(new DataChangedMessage(DataArea.Games | DataArea.Results, 1));
        var result = await publisher.LastAutoPublish!;
        Assert.Equal(LeaderboardPublishStatus.Published, result.Status);
        Assert.Single(h.LeaderboardTransport.Posts);
    }

    [Fact]
    public async Task Failed_auto_publish_shows_a_snackbar()
    {
        using var dir = new TempDir();
        await using var h = new Harness(dir);
        var publisher = h.Get<LeaderboardPublisher>();
        Configure(h);
        h.LeaderboardTransport.Respond = _ => throw new HttpRequestException("no network");

        h.Messenger.Send(new DataChangedMessage(DataArea.Results));
        await publisher.LastAutoPublish!;

        Assert.Contains("online leaderboard", h.Snackbar.Message);
    }

    [Fact]
    public async Task Settings_saves_the_link_and_publishes_now()
    {
        using var dir = new TempDir();
        await using var h = new Harness(dir);
        var vm = h.Get<SettingsViewModel>();
        await vm.OnAppearingAsync();
        Assert.Contains("Not set up", vm.LeaderboardStatus);

        vm.LeaderboardUrl = "  " + Url + " ";
        vm.LeaderboardSecret = "s3cret";
        await vm.PublishLeaderboardCommand.ExecuteAsync(null);

        Assert.Equal(Url, h.Get<AppSettings>().LeaderboardUrl);
        Assert.Single(h.LeaderboardTransport.Posts);
        Assert.StartsWith("Last published", vm.LeaderboardStatus);

        await vm.CopyLeaderboardLinkCommand.ExecuteAsync(null);
        Assert.Equal(Url, h.Clipboard.Text);
    }
}
