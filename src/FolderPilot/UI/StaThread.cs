namespace FolderPilot.UI;

/// <summary>Runs work that needs the Windows shell (COM, single-threaded apartment) off the UI thread.</summary>
internal static class StaThread
{
    public static Task<T> Run<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = "FolderPilot shell work" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
