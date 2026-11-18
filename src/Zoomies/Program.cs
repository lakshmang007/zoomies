using Velopack;
using Zoomies.Services;

namespace Zoomies;

/// <summary>
/// Entry point. Velopack goes first: when the installer runs the app for an install, update or uninstall step,
/// it handles that step and exits before any window opens.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build()
            .OnFirstRun(_ => App.IsFirstRun = true)
            .OnBeforeUninstallFastCallback(_ => WindowsIntegration.RemoveAll())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
