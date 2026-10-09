using FolderPilot.Core.History;
using FolderPilot.Core.Organize;

namespace FolderPilot.Core.Tests;

public class OrganizeTests
{
    private static readonly DateTime Old = new(2025, 3, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Plans_loose_files_by_type_and_leaves_risky_ones_alone()
    {
        using var tree = new TempTree();
        tree.File("report.pdf", modifiedUtc: Old);
        tree.File("photo.JPG", modifiedUtc: Old);
        tree.File("setup.exe", modifiedUtc: Old);
        tree.File("mystery.xyz", modifiedUtc: Old);
        tree.File("movie.mp4.crdownload", modifiedUtc: Old);   // still downloading
        tree.File("~$draft.docx", modifiedUtc: Old);           // Office lock file
        tree.File("Game.lnk", modifiedUtc: Old);               // shortcut
        tree.File("just-saved.txt");                           // changed seconds ago
        tree.File(@"Existing\inside.pdf", modifiedUtc: Old);   // not loose

        var plan = OrganizePlanner.Plan(tree.Root, new OrganizeOptions());

        var map = plan.Moves.ToDictionary(m => Path.GetFileName(m.Source), m => Path.GetRelativePath(tree.Root, m.Destination));
        Assert.Equal(new Dictionary<string, string>
        {
            ["report.pdf"] = @"Documents\report.pdf",
            ["photo.JPG"] = @"Images\photo.JPG",
            ["setup.exe"] = @"Installers\setup.exe",
            ["mystery.xyz"] = @"Other\mystery.xyz",
        }, map);
        Assert.Equal(4, plan.Skipped);
    }

    [Fact]
    public void Month_modes_use_the_file_date()
    {
        using var tree = new TempTree();
        tree.File("photo.png", modifiedUtc: Old);

        var byTypeMonth = OrganizePlanner.Plan(tree.Root, new OrganizeOptions { Mode = OrganizeMode.ByTypeAndMonth });
        var byMonth = OrganizePlanner.Plan(tree.Root, new OrganizeOptions { Mode = OrganizeMode.ByMonth });

        string local = Old.ToLocalTime().ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Path.Combine(tree.Root, "Images", local, "photo.png"), byTypeMonth.Moves[0].Destination);
        Assert.Equal(Path.Combine(tree.Root, local, "photo.png"), byMonth.Moves[0].Destination);
    }

    [Fact]
    public void Translated_folder_names_are_used()
    {
        using var tree = new TempTree();
        tree.File("photo.png", modifiedUtc: Old);

        var plan = OrganizePlanner.Plan(tree.Root, new OrganizeOptions(), c => c == FileCategory.Images ? "Imágenes" : c.ToString());

        Assert.Equal(Path.Combine(tree.Root, "Imágenes", "photo.png"), plan.Moves[0].Destination);
    }

    [Fact]
    public void Three_d_print_files_get_their_own_folder()
    {
        using var tree = new TempTree();
        tree.File("bracket.STL", modifiedUtc: Old);
        tree.File("benchy.3mf", modifiedUtc: Old);

        var plan = OrganizePlanner.Plan(tree.Root, new OrganizeOptions());

        Assert.All(plan.Moves, m => Assert.Equal(FileCategory.Models, m.Category));
        Assert.All(plan.Moves, m => Assert.Equal(tree.Path("Models"), Path.GetDirectoryName(m.Destination)));
    }

    [Fact]
    public void Custom_folder_names_win_over_the_category_folder()
    {
        using var tree = new TempTree();
        tree.File("bracket.STL", modifiedUtc: Old);
        tree.File("photo.png", modifiedUtc: Old);
        tree.File("README", modifiedUtc: Old);
        var custom = new Dictionary<string, string> { [".stl"] = "3D Prints", [""] = "No Extension" };

        var byType = OrganizePlanner.Plan(tree.Root, new OrganizeOptions { CustomFolders = custom });
        var byTypeAndMonth = OrganizePlanner.Plan(tree.Root, new OrganizeOptions { Mode = OrganizeMode.ByTypeAndMonth, CustomFolders = custom });
        var byMonth = OrganizePlanner.Plan(tree.Root, new OrganizeOptions { Mode = OrganizeMode.ByMonth, CustomFolders = custom });

        string Dest(OrganizePlan p, string name) =>
            Path.GetRelativePath(tree.Root, p.Moves.Single(m => Path.GetFileName(m.Source) == name).Destination);
        string month = Old.ToLocalTime().ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(@"3D Prints\bracket.STL", Dest(byType, "bracket.STL"));          // extension match ignores case
        Assert.Equal(@"Images\photo.png", Dest(byType, "photo.png"));                 // others keep the default
        Assert.Equal(@"No Extension\README", Dest(byType, "README"));
        Assert.Equal($@"3D Prints\{month}\bracket.STL", Dest(byTypeAndMonth, "bracket.STL"));
        Assert.Equal($@"{month}\bracket.STL", Dest(byMonth, "bracket.STL"));          // month mode has no type folders
    }

    [Fact]
    public void Retarget_updates_destinations_without_rescanning()
    {
        using var tree = new TempTree();
        tree.File("bracket.stl", modifiedUtc: Old);
        var custom = new Dictionary<string, string>();
        var options = new OrganizeOptions { CustomFolders = custom };
        var plan = OrganizePlanner.Plan(tree.Root, options);
        File.Delete(tree.Path("bracket.stl")); // proves nothing is read from disk again

        custom[".stl"] = "Printer";
        var retargeted = OrganizePlanner.Retarget(plan, options);

        Assert.Equal(tree.Path(@"Printer\bracket.stl"), retargeted.Moves.Single().Destination);
        Assert.Equal(tree.Path(@"Models\bracket.stl"), plan.Moves.Single().Destination); // original plan untouched
    }

    [Fact]
    public void Invalid_custom_names_fall_back_to_the_default()
    {
        using var tree = new TempTree();
        tree.File("bracket.stl", modifiedUtc: Old);

        var plan = OrganizePlanner.Plan(tree.Root, new OrganizeOptions { CustomFolders = new Dictionary<string, string> { [".stl"] = @"..\..\Windows" } });

        Assert.Equal(tree.Path(@"Models\bracket.stl"), plan.Moves.Single().Destination);
    }

    [Theory]
    [InlineData("3D Prints", true)]
    [InlineData("  Trimmed  ", true)]
    [InlineData("Imágenes", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a/b", false)]
    [InlineData(@"a\b", false)]
    [InlineData("..", false)]
    [InlineData("name.", false)]
    [InlineData("what?", false)]
    [InlineData("CON", false)]
    [InlineData("nul.txt", false)]
    [InlineData("Console", true)]
    public void Folder_names_are_validated(string name, bool valid) =>
        Assert.Equal(valid, OrganizePlanner.IsValidFolderName(name));

    [Fact]
    public void Execute_then_undo_restores_everything_and_removes_new_folders()
    {
        using var tree = new TempTree();
        tree.File("a.pdf", "A", Old);
        tree.File("b.png", "B", Old);
        tree.File(@"Documents\a.pdf", "already here", Old); // name clash in the destination
        var journal = new OperationJournal(tree.Path(@"data\history.json"));

        var plan = OrganizePlanner.Plan(tree.Root, new OrganizeOptions { Mode = OrganizeMode.ByTypeAndMonth });
        var result = FileMover.Execute(plan, journal, "Organized test");

        Assert.Equal(2, result.Moved);
        Assert.False(File.Exists(tree.Path("a.pdf")));
        string local = Old.ToLocalTime().ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(File.Exists(tree.Path($@"Documents\{local}\a.pdf")));

        var undo = journal.Undo(result.Entry!.Id);

        Assert.Equal(new UndoResult(2, 0), undo);
        Assert.Equal("A", File.ReadAllText(tree.Path("a.pdf")));
        Assert.Equal("B", File.ReadAllText(tree.Path("b.png")));
        Assert.False(Directory.Exists(tree.Path("Images")));              // created by organize → removed
        Assert.True(File.Exists(tree.Path(@"Documents\a.pdf")));           // existed before → kept
        Assert.False(Directory.Exists(tree.Path($@"Documents\{local}")));
        Assert.True(journal.Entries.Single().Undone);
    }

    [Fact]
    public void Clashing_names_get_a_number_instead_of_overwriting()
    {
        using var tree = new TempTree();
        tree.File("notes.txt", "new", Old);
        tree.File(@"Documents\notes.txt", "old", Old);

        var plan = OrganizePlanner.Plan(tree.Root, new OrganizeOptions());
        FileMover.Execute(plan, null, "t");

        Assert.Equal("old", File.ReadAllText(tree.Path(@"Documents\notes.txt")));
        Assert.Equal("new", File.ReadAllText(tree.Path(@"Documents\notes (2).txt")));
    }

    [Fact]
    public void Journal_survives_a_restart()
    {
        using var tree = new TempTree();
        tree.File("a.pdf", "A", Old);
        string file = tree.Path(@"data\history.json");
        var result = FileMover.Execute(OrganizePlanner.Plan(tree.Root, new OrganizeOptions()), new OperationJournal(file), "Organized");

        var reloaded = new OperationJournal(file);

        Assert.Equal(result.Entry!.Id, reloaded.LastUndoable!.Id);
        Assert.Equal(new UndoResult(1, 0), reloaded.Undo(result.Entry.Id));
        Assert.True(File.Exists(tree.Path("a.pdf")));
    }
}
