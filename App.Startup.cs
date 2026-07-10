using Clicky.Windows.Services;
using System.Windows;

namespace Clicky.Windows;

public partial class App
{
    private CompanionHost? _companionHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--visual-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("CLICKY_VISUAL_TEST", "1", EnvironmentVariableTarget.Process);
        }

        if (e.Args.Contains("--visual-test-ready", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("CLICKY_VISUAL_TEST_READY", "1", EnvironmentVariableTarget.Process);
        }

        if (e.Args.Contains("--visual-test-overlay", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("CLICKY_VISUAL_TEST_OVERLAY", "1", EnvironmentVariableTarget.Process);
        }

        if (e.Args.Contains("--visual-test-settings", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("CLICKY_VISUAL_TEST_SETTINGS", "1", EnvironmentVariableTarget.Process);
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _companionHost = new CompanionHost();
        _companionHost.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _companionHost?.Dispose();
        base.OnExit(e);
    }
}
