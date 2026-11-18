using System.Text;
using Zoomies.Core.Cleanup;
using Zoomies.Core.Insights;
using Zoomies.Core.Platform;

namespace Zoomies.Core.Tests;

public class CleanupTests
{
    private static readonly DateTime LongAgo = DateTime.UtcNow.AddDays(-400);

    [Fact]
    public void Unused_scan_finds_old_files_biggest_first()
    {
        using var tree = new TempTree();
        tree.File("old-small.bin", new string('a', 2_000), LongAgo);
        tree.File(@"sub\old-big.bin", new string('b', 9_000), LongAgo);
        tree.File("fresh.bin", new string('c', 9_000));
        tree.File("tiny-old.bin", "x", LongAgo);

        var found = UnusedFileScanner.Scan([tree.Root], new PathFilter(), minDaysUnused: 180, minSize: 1_000);

        Assert.Equal(["old-big.bin", "old-small.bin"], found.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void Recently_arrived_files_count_as_used_even_with_old_dates()
    {
        // e.g. an old photo copied to this PC yesterday: its "modified" date is old but it just arrived.
        var file = new WalkedFile("x", 10, CreationUtc: DateTime.UtcNow.AddDays(-1), LastWriteUtc: LongAgo, LastAccessUtc: LongAgo);
        Assert.True(UnusedFileScanner.LastUsed(file, useAccessTime: false) > DateTime.UtcNow.AddDays(-2));
    }

    [Fact]
    public void Duplicates_are_grouped_by_content_not_name()
    {
        using var tree = new TempTree();
        string same = new('z', 300_000); // bigger than the 2×64 KB sample, so the full hash is used
        tree.File("a.bin", same);
        tree.File(@"copy\a-copy.bin", same);
        tree.File(@"other\b.bin", same[..^1] + "y"); // same size, differs only at the end
        tree.File("small1.txt", "hello");
        tree.File("small2.txt", "hello");

        var groups = DuplicateFinder.Find([tree.Root], new PathFilter(), minSize: 1);

        Assert.Equal(2, groups.Count);
        var big = groups[0];
        Assert.Equal(300_000, big.Size);
        Assert.Equal(["a-copy.bin", "a.bin"], big.Files.Select(f => Path.GetFileName(f.Path)).Order());
        Assert.Equal(300_000, big.WastedBytes);
    }

    [Fact]
    public void Empty_folder_scan_reports_the_highest_empty_level_only()
    {
        using var tree = new TempTree();
        tree.Dir(@"empty\nested\deeper");
        tree.File(@"onlyini\desktop.ini");
        tree.File(@"keep\file.txt");
        tree.Dir(@"keep\empty-inside");
        var hidden = tree.File(@"hiddenonly\secret.txt");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var found = EmptyFolderScanner.Scan([tree.Root], new PathFilter());

        Assert.Equal(
            new[] { tree.Path("empty"), tree.Path(@"keep\empty-inside"), tree.Path("onlyini") }.Order(StringComparer.OrdinalIgnoreCase),
            found.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Old_installers_are_found_in_downloads()
    {
        using var tree = new TempTree();
        tree.File("old-setup.exe", "x", LongAgo);
        tree.File(@"Installers\older.msi", "x", LongAgo);
        tree.File("new-setup.exe", "x");
        tree.File("old.pdf", "x", LongAgo);

        var found = SuggestionEngine.OldInstallers(tree.Root);

        Assert.Equal(["old-setup.exe", "older.msi"], found.Select(f => Path.GetFileName(f.Path)).Order());
    }
}

public class RecycleBinTests
{
    [Fact]
    public void Parses_windows_10_info_records()
    {
        string path = @"C:\Users\me\Downloads\movie.mkv";
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(2L));
        data.AddRange(BitConverter.GetBytes(123_456L));
        var deleted = new DateTime(2026, 5, 1, 8, 30, 0, DateTimeKind.Utc);
        data.AddRange(BitConverter.GetBytes(deleted.ToFileTimeUtc()));
        data.AddRange(BitConverter.GetBytes(path.Length + 1));
        data.AddRange(Encoding.Unicode.GetBytes(path + "\0"));

        var info = RecycleBin.ParseInfo(data.ToArray());

        Assert.Equal((path, 123_456L, deleted), info);
    }

    [Fact]
    public void Parses_windows_7_info_records()
    {
        string path = @"D:\old\file.txt";
        var data = new byte[24 + 520];
        BitConverter.GetBytes(1L).CopyTo(data, 0);
        BitConverter.GetBytes(5L).CopyTo(data, 8);
        BitConverter.GetBytes(new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc()).CopyTo(data, 16);
        Encoding.Unicode.GetBytes(path).CopyTo(data, 24);

        Assert.Equal(path, RecycleBin.ParseInfo(data)!.Value.Path);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 65, 0 })]
    public void Rejects_garbage(byte[] data) => Assert.Null(RecycleBin.ParseInfo(data));

    [Fact]
    public void Network_paths_are_never_recycled_because_windows_would_delete_them_permanently() =>
        Assert.False(RecycleBin.CanRecycle(@"\\server\share\file.txt"));

    /// <summary>Uses the real Recycle Bin: recycles a temp file, restores it through the journal, and checks it is back.</summary>
    [Fact]
    public void Recycle_and_undo_round_trip()
    {
        using var tree = new TempTree();
        if (!RecycleBin.CanRecycle(tree.Root)) return; // e.g. %TEMP% on a network drive
        string file = tree.File("unwanted.txt", "keep me safe");
        var journal = new History.OperationJournal(tree.Path("history.json"));

        var outcome = Recycler.Recycle([(file, 12L)], journal, "Cleanup test");
        Assert.Equal(1, outcome.Recycled);
        Assert.False(File.Exists(file));

        var undo = journal.Undo(outcome.Entry!.Id);

        Assert.Equal(1, undo.Restored);
        Assert.Equal("keep me safe", File.ReadAllText(file));
    }
}
