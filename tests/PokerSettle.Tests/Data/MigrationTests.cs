using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using PokerSettle.Core.Data;
using PokerSettle.Core.Stats;

namespace PokerSettle.Tests.Data;

public class MigrationTests
{
    [Fact]
    public async Task New_database_is_created_at_the_latest_schema_with_wal_and_foreign_keys()
    {
        using var dir = new TempDir();
        await using var store = await Stores.OpenAsync(dir);

        var info = await store.GetInfoAsync();

        Assert.Equal(Migrations.Latest, info.SchemaVersion);
        Assert.Null(store.Recovery);
        Assert.Equal("wal", Stores.Scalar(store.DatabasePath, "PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task Version_1_database_is_upgraded_and_nets_are_backfilled()
    {
        using var dir = new TempDir();
        var path = dir.File("poker-settle.db");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            Migrations.Apply(connection, NullLogger.Instance, targetVersion: 1);
            Assert.Equal(1, Migrations.GetVersion(connection));
            Stores.Exec(connection, """
                INSERT INTO players (id, name, name_key, color, created_at) VALUES
                    (1, 'Amit', 'amit', '#1E6B47', 0), (2, 'Rahul', 'rahul', '#1E6B47', 0), (3, 'Priya', 'priya', '#1E6B47', 0);
                INSERT INTO games (id, title, date, status, default_buy_in, created_at, updated_at, finalized_at)
                    VALUES (1, 'Old night', '2025-01-04', 1, 50000, 0, 0, 0);
                INSERT INTO entries (game_id, player_id, position, cash_out) VALUES (1, 1, 0, 0), (1, 2, 1, 120000), (1, 3, 2, 30000);
                INSERT INTO buy_ins (game_id, player_id, amount, at) VALUES (1, 1, 50000, 0), (1, 2, 50000, 0), (1, 3, 50000, 0);
                INSERT INTO transfers (game_id, from_id, to_id, amount, is_paid) VALUES (1, 1, 2, 50000, 1), (1, 3, 2, 20000, 0);
                """);
        }

        await using var store = await Stores.OpenAsync(dir);

        Assert.Null(store.Recovery);
        Assert.Equal(Migrations.Latest, (await store.GetInfoAsync()).SchemaVersion);
        Assert.Equal(2L, Stores.Scalar(path, "PRAGMA user_version;"));
        var board = (await store.GetBoardAsync(DateRange.All, includeArchived: true)).ToDictionary(r => r.Name);
        Assert.Equal(70_000, board["Rahul"].Net);
        Assert.Equal(-50_000, board["Amit"].Net);
        Assert.Equal(-20_000, board["Priya"].Net);
        var game = await store.GetGameAsync(1);
        Assert.NotNull(game);
        Assert.Equal(2, game.Transfers.Count);
        Assert.True(game.Transfers.Single(t => t.FromId == 1).IsPaid);
    }

    [Fact]
    public async Task Migration_is_skipped_when_already_at_latest()
    {
        using var dir = new TempDir();
        await using (var first = await Stores.OpenAsync(dir))
        {
            await first.AddPlayerAsync(new PlayerDraft("Amit", null, null, null, null, null));
        }

        await using var second = await Stores.OpenAsync(dir);

        Assert.Single(await second.GetPlayersAsync());
        Assert.Null(second.Recovery);
    }

    [Fact]
    public async Task Database_from_a_newer_app_is_moved_aside_not_destroyed()
    {
        using var dir = new TempDir();
        await using (var first = await Stores.OpenAsync(dir))
        {
            await first.AddPlayerAsync(new PlayerDraft("Amit", null, null, null, null, null));
        }
        using (var connection = new SqliteConnection($"Data Source={dir.File("poker-settle.db")};Pooling=False"))
        {
            connection.Open();
            Stores.Exec(connection, "PRAGMA user_version = 99;");
        }

        await using var store = await Stores.OpenAsync(dir);

        Assert.NotNull(store.Recovery);
        Assert.Contains("newer version", store.Recovery.Reason);
        Assert.True(File.Exists(store.Recovery.MovedTo));
        Assert.Equal(99L, Stores.Scalar(store.Recovery.MovedTo!, "PRAGMA user_version;"));
        Assert.Empty(await store.GetPlayersAsync());
    }
}
