using FolderPilot.Core.Indexing;

namespace FolderPilot.Core.Tests;

public class TextMatchTests
{
    [Theory]
    [InlineData("report.pdf", "report.pdf", 100)]
    [InlineData("report.pdf", "report", 95)]
    public void Exact_matches_score_highest(string name, string token, double expected) =>
        Assert.Equal(expected, TextMatch.Score(name, token));

    [Fact]
    public void Prefix_beats_word_start_beats_substring()
    {
        double prefix = TextMatch.Score("Projects", "proj");
        double wordStart = TextMatch.Score("my-projects", "proj");
        double inside = TextMatch.Score("subprojects", "proj");
        Assert.True(prefix > wordStart, $"{prefix} > {wordStart}");
        Assert.True(wordStart > inside, $"{wordStart} > {inside}");
    }

    [Fact]
    public void Initials_match_fuzzily_but_below_real_substrings()
    {
        double fuzzy = TextMatch.Score("final_year_project", "fyp");
        Assert.True(fuzzy > 0);
        Assert.True(fuzzy < TextMatch.Score("final_year_project", "year"));
    }

    [Fact]
    public void Random_letters_do_not_match()
    {
        Assert.Equal(0, TextMatch.Score("holiday photos", "zq"));
        // The first letter must start a word, so "jt" does not match pro[j]ec[t].
        Assert.Equal(0, TextMatch.Score("project", "jt"));
    }

    [Fact]
    public void CamelCase_and_digits_are_word_starts()
    {
        Assert.True(TextMatch.IsWordStart("myReport", 2));
        Assert.True(TextMatch.IsWordStart("invoice2024", 7));
        Assert.False(TextMatch.IsWordStart("invoice", 3));
    }
}

public class FileIndexTests
{
    private static FileIndex BuildIndex(TempTree tree) =>
        FileIndex.Build([tree.Root], new PathFilter(), maxEntries: 100_000);

    [Fact]
    public void Finds_files_and_folders_with_full_paths()
    {
        using var tree = new TempTree();
        string notes = tree.File(@"Documents\College\notes.docx");
        tree.File(@"Documents\Work\budget.xlsx");

        var index = BuildIndex(tree);
        var hits = index.Search("notes", 10);

        Assert.Contains(hits, h => h.Path.Equals(notes, StringComparison.OrdinalIgnoreCase) && !h.IsDirectory);
        Assert.Equal(tree.Path(@"Documents\College"), index.Search("college", 5, SearchScope.FoldersOnly)[0].Path, ignoreCase: true);
    }

    [Fact]
    public void Extra_words_narrow_by_parent_folder()
    {
        using var tree = new TempTree();
        string college = tree.File(@"College\notes.txt");
        tree.File(@"Work\notes.txt");

        var hits = BuildIndex(tree).Search("college notes", 10);

        Assert.Single(hits, h => !h.IsDirectory);
        Assert.Equal(college, hits.First(h => !h.IsDirectory).Path, ignoreCase: true);
    }

    [Fact]
    public void Skips_excluded_and_dot_folders()
    {
        using var tree = new TempTree();
        tree.File(@"app\node_modules\lib\target.js");
        tree.File(@".git\objects\target.pack");
        string real = tree.File(@"app\src\target.cs");

        var hits = BuildIndex(tree).Search("target", 10);

        Assert.Equal([real], hits.Select(h => h.Path), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Live_updates_add_rename_and_delete()
    {
        using var tree = new TempTree();
        tree.Dir("Inbox");
        var index = BuildIndex(tree);

        string created = tree.File(@"Inbox\invoice.pdf");
        Assert.True(index.ApplyCreated(created));
        Assert.Single(index.Search("invoice", 5));

        string renamed = tree.Path(@"Inbox\receipt.pdf");
        File.Move(created, renamed);
        index.ApplyRenamed(created, renamed);
        Assert.Empty(index.Search("invoice", 5));
        Assert.Single(index.Search("receipt", 5));

        // Deleting a folder hides everything inside it.
        Directory.Delete(tree.Path("Inbox"), true);
        index.ApplyDeleted(tree.Path("Inbox"));
        Assert.Empty(index.Search("receipt", 5));
    }

    [Fact]
    public void A_folder_moved_in_is_indexed_with_its_contents()
    {
        using var tree = new TempTree();
        tree.Dir("Projects");
        var index = BuildIndex(tree);

        using var outside = new TempTree();
        outside.File(@"thesis\chapter1.docx");
        Directory.Move(outside.Path("thesis"), tree.Path(@"Projects\thesis"));
        index.ApplyCreated(tree.Path(@"Projects\thesis"));

        Assert.Single(index.Search("chapter1", 5));
    }

    [Fact]
    public void Boost_reorders_results()
    {
        using var tree = new TempTree();
        string a = tree.Dir(@"one\reports");
        string b = tree.Dir(@"two\reports");
        var index = BuildIndex(tree);

        var hits = index.Search("reports", 5, SearchScope.FoldersOnly,
            boost: p => p.Equals(b, StringComparison.OrdinalIgnoreCase) ? 30 : 0);

        Assert.Equal(b, hits[0].Path, ignoreCase: true);
        Assert.Contains(hits, h => h.Path.Equals(a, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Memory_per_entry_stays_small()
    {
        using var tree = new TempTree();
        for (int i = 0; i < 2000; i++) tree.File($@"d{i % 40}\file_{i}.txt");
        var index = BuildIndex(tree);

        Assert.Equal(2000 + 40 + 1, index.Count);
        // ~15 bytes of arrays + ~12 characters of name per entry.
        Assert.True(index.ApproximateBytes / index.Count < 60, $"{index.ApproximateBytes / index.Count} bytes per entry");
    }
}
