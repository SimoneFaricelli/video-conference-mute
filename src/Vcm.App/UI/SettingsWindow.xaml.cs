using System.Windows;
using Microsoft.Win32;
using Vcm.App.Localization;
using Vcm.App.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Vcm.App.UI;

public partial class SettingsWindow : FluentWindow
{
    internal SettingsWindow(AppController controller)
    {
        ViewModel = new SettingsViewModel(controller);
        DataContext = ViewModel;
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        Closed += (_, _) => ViewModel.Detach();
    }

    internal SettingsViewModel ViewModel { get; }

    internal ContentDialogHost DialogHost => RootDialogHost;

    internal void ReloadFromController() => ViewModel.Reload();

    private void OnBrowseOverlay(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"{Loc.Get("ImagesFilter")} (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.CameraOverlayImagePath = dialog.FileName;
        }
    }

    private void OnClearOverlay(object sender, RoutedEventArgs e) => ViewModel.CameraOverlayImagePath = string.Empty;
}
