using System.Text.Json;
using Microsoft.Extensions.Logging;
using PokerSettle.Core.Diagnostics;
using PokerSettle.Core.Logging;

namespace PokerSettle.Tests.Logging;

public class LoggerTests
{
    private static (ILoggerFactory Factory, JsonLogWriter Writer, LogFileStore Store) Create(TempDir dir, bool enabled, long maxBytes = LogFileStore.DefaultMaxFileBytes)
    {
        var store = new LogFileStore(dir.File("logs"), maxBytes);
        var writer = new JsonLogWriter(store, TimeSpan.FromMilliseconds(50));
        var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new JsonFileLoggerProvider(new LogSwitch(enabled), writer));
        });
        return (factory, writer, store);
    }

    [Fact]
    public async Task Off_writes_nothing_to_disk()
    {
        using var dir = new TempDir();
        var (factory, writer, store) = Create(dir, enabled: false);
        var logger = factory.CreateLogger("PokerSettle.Test");

        Assert.False(logger.IsEnabled(LogLevel.Critical));
        for (var i = 0; i < 100; i++)
        {
            logger.BuyInAdded(1, 2, 50_000, i);
            logger.CommandFailed(new InvalidOperationException("boom"), "Test");
        }
        writer.Flush();
        await Task.Delay(150);

        Assert.False(Directory.Exists(store.DirectoryPath));
        Assert.Empty(store.Files());
    }

    [Fact]
    public async Task On_writes_structured_json_lines_in_background_batches()
    {
        using var dir = new TempDir();
        var (factory, _, store) = Create(dir, enabled: true);
        var logger = factory.CreateLogger("PokerSettle.Core.Data.DataStore");

        logger.BuyInAdded(7, 3, 50_000, 42);
        logger.CommandFailed(new InvalidOperationException("boom"), "LiveGame.AddBuyIn");

        // No explicit flush: the background loop writes within its interval.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (store.ReadAll().Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(25);

        var records = store.ReadAll();
        Assert.Equal(2, records.Count);
        var buyIn = records[0];
        Assert.Equal("Information", buyIn.Level);
        Assert.Equal("DataStore", buyIn.Category);
        Assert.Equal("Buy-in 42: player 3 +50000 in game 7", buyIn.Message);
        Assert.Equal(7, buyIn.Data!["GameId"]!.GetValue<long>());
        Assert.Equal(50_000, buyIn.Data["Amount"]!.GetValue<long>());
        Assert.Equal("Error", records[1].Level);
        Assert.Contains("boom", records[1].Data!["exception"]!.GetValue<string>());

        foreach (var line in File.ReadAllLines(store.CurrentPath))
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("ts", out _));
        }
    }

    [Fact]
    public void Phone_and_upi_values_are_redacted_even_if_passed_by_mistake()
    {
        using var dir = new TempDir();
        var (factory, writer, store) = Create(dir, enabled: true);
        var logger = factory.CreateLogger("Test");

        logger.LogInformation("Saved {PlayerPhone} and {UpiId}", "+91 98765 43210", "amit@okaxis");
        writer.Flush();

        var record = Assert.Single(store.ReadAll());
        Assert.Equal("[redacted]", record.Data!["PlayerPhone"]!.GetValue<string>());
        Assert.Equal("[redacted]", record.Data["UpiId"]!.GetValue<string>());
        Assert.DoesNotContain("98765", File.ReadAllText(store.CurrentPath));
        Assert.DoesNotContain("okaxis", File.ReadAllText(store.CurrentPath));
    }

    [Fact]
    public void Files_roll_at_the_cap_and_keep_at_most_three()
    {
        using var dir = new TempDir();
        const long cap = 2048;
        var store = new LogFileStore(dir.File("logs"), cap, maxFiles: 3);
        var lines = Enumerable.Range(0, 400).Select(i => $"{{\"ts\":\"2026-10-24T21:00:00+05:30\",\"level\":\"Information\",\"category\":\"T\",\"message\":\"line {i:D4}\"}}").ToList();

        foreach (var chunk in lines.Chunk(7)) store.Append(chunk);

        var files = store.Files();
        Assert.Equal(3, files.Count);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length <= cap, $"{f} is {new FileInfo(f).Length} bytes"));
        var kept = store.ReadAll().Select(r => r.Message).ToList();
        Assert.Equal("line 0399", kept[^1]);
        Assert.DoesNotContain("line 0000", kept);
        // Oldest-first ordering across the rolled files.
        Assert.Equal(kept.OrderBy(m => m, StringComparer.Ordinal), kept);
    }

    [Fact]
    public void Default_cap_is_three_files_of_one_megabyte()
    {
        Assert.Equal(1024 * 1024, LogFileStore.DefaultMaxFileBytes);
        Assert.Equal(3, LogFileStore.DefaultMaxFiles);
    }

    [Fact]
    public void Crash_marker_detects_a_session_that_never_went_to_the_background()
    {
        using var dir = new TempDir();
        var first = new CrashMarker(dir.Path);
        first.OnStartup();
        Assert.False(first.PreviousSessionCrashed);
        // App killed while in the foreground: marker left behind, and it holds no details.
        Assert.Equal(0, new FileInfo(first.MarkerPath).Length);

        var second = new CrashMarker(dir.Path);
        second.OnStartup();
        Assert.True(second.PreviousSessionCrashed);
        second.MarkInactive();

        var third = new CrashMarker(dir.Path);
        third.OnStartup();
        Assert.False(third.PreviousSessionCrashed);
    }
}
