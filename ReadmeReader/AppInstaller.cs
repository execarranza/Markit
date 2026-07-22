using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace ReadmeReader;

internal static class AppInstaller
{
    private const string AppFolderName = "Markit";
    private const string AppFileName = "Markit.exe";
    private const string ProgId = "Markit.MarkdownFile";
    private const string LegacyAppFolderName = "Readme Reader";
    private const string LegacyAppFileName = "ReadmeReader.exe";
    private const string LegacyProgId = "ReadmeReader.MarkdownFile";
    private const string AppPathsRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\App Paths\Markit.exe";
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0;
    private static readonly string[] SupportedExtensions = [".md", ".markdown", ".mdown"];

    public static string GetDefaultInstallDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName);
    }

    public static string GetInstallPath(string? installDirectory = null)
    {
        return Path.Combine(
            string.IsNullOrWhiteSpace(installDirectory) ? GetDefaultInstallDirectory() : installDirectory,
            AppFileName);
    }

    public static string InstallCurrentExecutable(bool showConfirmation, Action<int, string>? reportProgress = null, string? installDirectory = null)
    {
        reportProgress?.Invoke(5, "Preparando instalacion...");
        var source = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("No se pudo ubicar el ejecutable actual.");

        var installPath = GetInstallPath(installDirectory);
        var targetDirectory = Path.GetDirectoryName(installPath)
            ?? throw new InvalidOperationException("No se pudo preparar la carpeta de instalacion.");

        reportProgress?.Invoke(18, "Creando carpeta local...");
        Directory.CreateDirectory(targetDirectory);

        if (!PathsEqual(source, installPath))
        {
            reportProgress?.Invoke(34, "Cerrando versiones anteriores...");
            CloseRunningInstalledApp(installPath);
            reportProgress?.Invoke(52, "Copiando Markit...");
            File.Copy(source, installPath, overwrite: true);
        }

        reportProgress?.Invoke(68, "Limpiando instalacion anterior...");
        CleanupLegacyInstall();
        reportProgress?.Invoke(78, "Registrando aplicacion...");
        RegisterApplicationPath(installPath);
        reportProgress?.Invoke(88, "Asociando archivos Markdown...");
        RegisterFileAssociation(installPath);
        reportProgress?.Invoke(96, "Actualizando Windows...");
        NotifyShellAssociationsChanged();
        reportProgress?.Invoke(100, "Instalacion completa.");

        if (showConfirmation)
        {
            MessageBox.Show(
                $"markit se instalo en:\n{installPath}\n\nLos archivos Markdown ya pueden abrirse con esta app.",
                "Instalacion completa",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        return installPath;
    }

    public static void Uninstall(bool showConfirmation)
    {
        foreach (var extension in SupportedExtensions)
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{extension}", throwOnMissingSubKey: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{LegacyProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(AppPathsRegistryPath, throwOnMissingSubKey: false);
        NotifyShellAssociationsChanged();

        if (showConfirmation)
        {
            MessageBox.Show(
                "Se quito la asociacion de archivos Markdown.",
                "Desinstalacion completa",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static void RegisterFileAssociation(string executablePath)
    {
        foreach (var extension in SupportedExtensions)
        {
            RegisterExtension(extension);
        }

        using var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
        progId?.SetValue(null, "markit Markdown document");

        using var defaultIcon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon");
        defaultIcon?.SetValue(null, $"\"{executablePath}\",0");

        using var command = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        command?.SetValue(null, $"\"{executablePath}\" \"%1\"");
    }

    private static void RegisterExtension(string extension)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}");
        key?.SetValue(null, ProgId);
        key?.SetValue("Content Type", "text/markdown");
        key?.SetValue("PerceivedType", "text");
    }

    private static void RegisterApplicationPath(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppPathsRegistryPath);
        key?.SetValue(null, executablePath);
        key?.SetValue("Path", Path.GetDirectoryName(executablePath) ?? string.Empty);
    }

    private static void CleanupLegacyInstall()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{LegacyProgId}", throwOnMissingSubKey: false);

        var legacyInstallPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyAppFolderName,
            LegacyAppFileName);
        if (!File.Exists(legacyInstallPath))
        {
            return;
        }

        CloseRunningInstalledApp(legacyInstallPath);
        try
        {
            File.Delete(legacyInstallPath);
        }
        catch
        {
            // A locked legacy executable should not block the new Markit installation.
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void CloseRunningInstalledApp(string installPath)
    {
        var currentProcessId = Environment.ProcessId;
        var normalizedInstallPath = Path.GetFullPath(installPath);

        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppFileName)))
        {
            try
            {
                if (process.Id == currentProcessId || process.MainModule?.FileName is null)
                {
                    continue;
                }

                if (!PathsEqual(process.MainModule.FileName, normalizedInstallPath))
                {
                    continue;
                }

                process.CloseMainWindow();
                if (!process.WaitForExit(2500))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2500);
                }
            }
            catch
            {
                // If Windows denies process details, leave it alone and let the copy report the real issue.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void NotifyShellAssociationsChanged()
    {
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
