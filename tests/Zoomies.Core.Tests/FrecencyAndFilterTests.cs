using Zoomies.Core.Frecency;
using Zoomies.Core.Workspaces;

namespace Zoomies.Core.Tests;

public class FrecencyTests
{
    [Fact]
    public void Recent_visits_outrank_old_frequent_ones()
    {
        using var tree = new TempTree();
        var store = new FrecencyStore(tree.Path("f.json"));
        var now = DateTime.UtcNow;
        for (int i = 0; i < 6; i++) store.Visit(@"C:\old\project", whenUtc: now.AddDays(-60));
        store.Visit(@"C:\new\project", whenUtc: now.AddMinutes(-5));
        store.Visit(@"C:\new\project", whenUtc: now.AddMinutes(-2));

        Assert.Equal(@"C:\new\project", store.Top(1, now)[0].Path);
    }

    [Fact]
    public void Match_needs_words_in_order_and_last_word_in_folder_name()
    {
        using var tree = new TempTree();
        var store = new FrecencyStore(tree.Path("f.json"));
        store.Visit(@"D:\Projects\shop\api");
        store.Visit(@"D:\api-docs\shop");

        var hits = store.Match("proj api", 5);

        Assert.Equal([@"D:\Projects\shop\api"], hits.Select(h => h.Path));
    }

    [Fact]
    public void Saves_and_reloads()
    {
        using var tree = new TempTree();
        var store = new FrecencyStore(tree.Path("f.json"));
        store.Visit(@"C:\a");
        store.SaveIfDirty();

        Assert.True(new FrecencyStore(tree.Path("f.json")).Score(@"C:\a\") > 0);
    }

    [Fact]
    public void Boost_is_capped()
    {
        Assert.Equal(0, FrecencyStore.Boost(0));
        Assert.Equal(40, FrecencyStore.Boost(1_000_000));
    }
}

public class PathFilterTests
{
    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\x.txt", true)]
    [InlineData(@"C:\Users\me\code\node_modules\a\b.js", true)]
    [InlineData(@"C:\Users\me\.vscode\settings.json", true)]
    [InlineData(@"C:\Windows\notepad.exe", true)]
    [InlineData(@"C:\Users\me\Documents\Windows tips\Windows\a.txt", false)] // "Windows" only skipped at a drive root
    [InlineData(@"C:\Users\me\Documents\report.docx", false)]
    [InlineData(@"C:\Users\me\.profile", false)] // a dot-file, not a dot-folder
    public void Excludes_system_and_tool_folders(string path, bool excluded) =>
        Assert.Equal(excluded, new PathFilter().IsExcludedPath(path));

    [Fact]
    public void Remove_nested_keeps_outermost_folders()
    {
        var roots = PathUtil.RemoveNested([@"C:\Users\me\Documents", @"C:\Users\me", @"C:\Users\me\", @"D:\Data"]);
        Assert.Equal([@"C:\Users\me", @"D:\Data"], roots.Order());
    }

    [Fact]
    public void Online_only_cloud_files_are_detected() =>
        Assert.True(CloudFiles.IsOnlineOnly((FileAttributes)0x00400000 | FileAttributes.Archive));
}

public class WorkspaceTests
{
    [Fact]
    public void Save_replaces_by_name_and_dedupes_folders()
    {
        using var tree = new TempTree();
        var store = new WorkspaceStore(tree.Path("w.json"));
        store.Save("Thesis", [@"C:\a", @"C:\b"]);
        store.Save("thesis", [@"C:\c", @"C:\C", @"C:\d"]);

        var all = new WorkspaceStore(tree.Path("w.json")).All;

        Assert.Single(all);
        Assert.Equal([@"C:\c", @"C:\d"], all[0].Folders);
    }
}
