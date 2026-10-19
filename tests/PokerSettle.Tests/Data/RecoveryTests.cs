using PokerSettle.Core.Data;

namespace PokerSettle.Tests.Data;

public class RecoveryTests
{
    [Fact]
    public async Task Garbage_file_is_moved_aside_and_a_fresh_database_starts()
    {
        using var dir = new TempDir();
        var path = dir.File("poker-settle.db");
        var garbage = new byte[8192];
        new Random(7).NextBytes(garbage);
        await File.WriteAllBytesAsync(path, garbage);

        await using var store = await Stores.OpenAsync(dir);

        Assert.NotNull(store.Recovery);
        Assert.False(store.Recovery.InMemory);
        Assert.StartsWith(path + ".corrupt-", store.Recovery.MovedTo);
        Assert.Equal(garbage, await File.ReadAllBytesAsync(store.Recovery.MovedTo!));
        // The fresh database works.
        var player = await store.AddPlayerAsync(new PlayerDraft("Amit", null, null, null, null, null));
        Assert.True(player.Id > 0);
    }

    [Fact]
    public async Task Database_failing_the_integrity_check_is_moved_aside()
    {
        using var dir = new TempDir();
        await using (var store = await Stores.OpenAsync(dir))
        {
            for (var i = 0; i < 300; i++)
                await store.AddPlayerAsync(new PlayerDraft($"Player number {i} with a long name", "nick", null, null, "notes notes notes", null));
        }
        var path = dir.File("poker-settle.db");
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.Length > 4096 * 4, "expected a multi-page database");
        // Scribble over the second page (a b-tree page), leaving the header intact.
        for (var i = 4096; i < 8192; i++) bytes[i] = 0x5A;
        await File.WriteAllBytesAsync(path, bytes);

        await using var recovered = await Stores.OpenAsync(dir);

        Assert.NotNull(recovered.Recovery);
        Assert.True(File.Exists(recovered.Recovery.MovedTo));
        Assert.Empty(await recovered.GetPlayersAsync());
    }

    [Fact]
    public async Task Snapshots_are_consistent_copies_and_only_the_last_five_are_kept()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        await using var store = await Stores.OpenAsync(dir, clock);
        await store.AddPlayerAsync(new PlayerDraft("Amit", null, null, null, null, null));

        string last = "";
        for (var i = 0; i < 7; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            last = await store.CreateSnapshotAsync("test");
        }

        var snapshots = store.ListSnapshots();
        Assert.Equal(5, snapshots.Count);
        Assert.Equal(last, snapshots[0].FullName);
        Assert.Equal(1L, Stores.Scalar(last, "SELECT COUNT(*) FROM players;"));
        Assert.Equal("ok", Stores.Scalar(last, "PRAGMA integrity_check;"));
    }
}
