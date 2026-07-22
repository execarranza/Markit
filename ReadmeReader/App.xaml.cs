using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace ReadmeReader;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        var executableName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        var installerMode = IsInstallerExecutable(executableName)
            && e.Args.Length == 0;

        if (installerMode || e.Args.Any(arg => string.Equals(arg, "--install", StringComparison.OrdinalIgnoreCase)))
        {
            var installerWindow = new InstallerWindow();
            if (installerWindow.ShowDialog() == true
                && installerWindow.LaunchRequested
                && installerWindow.InstallPath is not null
                && File.Exists(installerWindow.InstallPath))
            {
                Process.Start(new ProcessStartInfo(installerWindow.InstallPath) { UseShellExecute = true });
            }

            Shutdown();
            return;
        }

        if (e.Args.Any(arg => string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            AppInstaller.Uninstall(showConfirmation: true);
            Shutdown();
            return;
        }

        var initialFile = e.Args.FirstOrDefault(arg =>
            !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase)
            && DocumentFileService.Exists(arg)
            && DocumentFileService.IsSupportedDocument(arg));

        var window = new MainWindow(initialFile);
        window.Show();
    }

    private static bool IsInstallerExecutable(string executableName)
    {
        return string.Equals(executableName, "ReadmeReaderInstaller", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executableName, "MarkitInstaller", StringComparison.OrdinalIgnoreCase);
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"markit encontro un problema inesperado.\n\nDetalle:\n{e.Exception.Message}",
            "Error inesperado",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
