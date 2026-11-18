using Zoomies.Core.History;
using Zoomies.Core.Platform;

namespace Zoomies.Core.Organize;

public readonly record struct MoveResult(int Moved, int Failed, long Bytes, JournalEntry? Entry);

/// <summary>Carries out an <see cref="OrganizePlan"/> and records every move so it can be undone.</summary>
public static class FileMover
{
    public static MoveResult Execute(OrganizePlan plan, OperationJournal? journal, string title,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var entry = new JournalEntry { Kind = JournalKind.Move, Title = title };
        var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int moved = 0, failed = 0;
        long bytes = 0;

        foreach (var move in plan.Moves)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!File.Exists(move.Source)) { failed++; continue; }
                string dir = Path.GetDirectoryName(move.Destination)!;
                if (!Directory.Exists(dir))
                {
                    // Record each missing level so undo can remove "Images\2026-10" and then "Images".
                    for (string? d = dir; d is not null && !Directory.Exists(d); d = Path.GetDirectoryName(d))
                        createdDirs.Add(d);
                    Directory.CreateDirectory(dir);
                }
                string destination = PathUtil.UniquePath(move.Destination);
                File.Move(move.Source, destination);
                entry.Ops.Add(new FileOp { From = move.Source, To = destination, Size = move.Size });
                moved++;
                bytes += move.Size;
                progress?.Report(moved);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Usually the file is open in another app. It simply stays where it is.
                failed++;
            }
        }

        entry.CreatedDirectories = [.. createdDirs];
        journal?.Add(entry);
        Shell.NotifyFolderChanged(plan.Folder);
        return new MoveResult(moved, failed, bytes, entry.Ops.Count > 0 ? entry : null);
    }
}
