using System.Windows;
using Microsoft.Win32;

namespace ReadmeReader;

public partial class InstallerWindow : Window
{
    private string? installPath;
    private string installDirectory;
    private bool installCompleted;

    public InstallerWindow()
    {
        InitializeComponent();
        installDirectory = AppInstaller.GetDefaultInstallDirectory();
        UpdateInstallPath();
    }

    public string? InstallPath => installPath;

    public bool LaunchRequested => LaunchCheckBox.IsChecked == true;

    private void AcceptCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = AcceptCheckBox.IsChecked == true;
        StatusText.Text = InstallButton.IsEnabled ? "Listo para instalar." : "Esperando confirmacion.";
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Elegir carpeta de instalacion",
            InitialDirectory = installDirectory,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        installDirectory = dialog.FolderName;
        UpdateInstallPath();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (installCompleted)
        {
            DialogResult = true;
            return;
        }

        SetInstallingState();

        try
        {
            installPath = await Task.Run(() => AppInstaller.InstallCurrentExecutable(
                showConfirmation: false,
                reportProgress: ReportProgress,
                installDirectory: installDirectory));

            ReportProgress(100, "Instalacion completa.");
            LaunchCheckBox.Visibility = Visibility.Visible;
            StatusText.Text = "Markit quedo instalado correctamente.";
            InstallButton.Content = "Finalizar";
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            installCompleted = true;
        }
        catch (Exception ex)
        {
            InstallProgressBar.Value = 0;
            ProgressText.Text = "No se pudo completar la instalacion.";
            StatusText.Text = ex.Message;
            InstallButton.Content = "Reintentar";
            AcceptCheckBox.IsEnabled = true;
            BrowseButton.IsEnabled = true;
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SetInstallingState()
    {
        AcceptCheckBox.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        InstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        InstallButton.Content = "Instalando...";
        StatusText.Text = "Instalando Markit...";
        InstallProgressBar.Value = 0;
    }

    private void UpdateInstallPath()
    {
        InstallPathText.Text = installDirectory;
        InstallPathText.ToolTip = AppInstaller.GetInstallPath(installDirectory);
    }

    private void ReportProgress(int value, string message)
    {
        Dispatcher.Invoke(() =>
        {
            InstallProgressBar.Value = value;
            ProgressText.Text = message;
        });
    }
}
