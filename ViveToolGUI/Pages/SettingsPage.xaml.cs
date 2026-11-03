using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using ViVeToolGUI.AppWindows;
using Windows.ApplicationModel;
using Windows.Storage;

namespace ViVeToolGUI.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
        private bool _isInitializing = true;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadUI();
            LoadAppInfo();
            _isInitializing = false;
        }

        private void LoadUI()
        {
            string theme = localSettings.Values["AppTheme"] as string ?? "System";
            RbTheme.SelectedIndex = theme switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0
            };
            string material = localSettings.Values["AppMaterial"] as string ?? "MicaAlt";
            RbMaterial.SelectedIndex = material switch
            {
                "MicaAlt" => 1,
                "Acrylic" => 2,
                _ => 0
            };
            string pos = localSettings.Values["PanePosition"] as string ?? "Left";
            PanePositionCombo.SelectedIndex = pos == "Top" ? 1 : 0;
            bool sound = localSettings.Values["EnableSound"] is bool b ? b : true;
            if (localSettings.Values["EnableSound"] == null)
                localSettings.Values["EnableSound"] = true;
            SoundToggle.IsOn = sound;
            bool notification = localSettings.Values["EnableQueryNotification"] is bool bn ? bn : true;
            if (localSettings.Values["EnableQueryNotification"] == null)
                localSettings.Values["EnableQueryNotification"] = true;
            NotificationToggle.IsOn = notification;
        }

        public void LoadAppInfo()
        {
            try
            {
                TxtAppName.Text = Package.Current.DisplayName;

                var v = Package.Current.Id.Version;
                TxtVersion.Text = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";

                ImgAppIcon.Source = new BitmapImage(Package.Current.Logo);
                TxtCopyright.Text = $"©{DateTime.Now.Year} {Package.Current.PublisherDisplayName}。保留所有权利。";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadAppInfo 错误: {ex.Message}");
            }
        }

        private void OpenExternalLink(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance.OpenExternalLink(sender, e);
        }

        private void RbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            string value = RbTheme.SelectedIndex switch
            {
                1 => "Light",
                2 => "Dark",
                _ => "System"
            };

            var theme = RbTheme.SelectedIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            localSettings.Values["AppTheme"] = value;
            AppThemeManager.CurrentTheme = theme;

            if (App.MainWindow?.Content is FrameworkElement root)
                root.RequestedTheme = theme;

            TextFileViewerWindow.ApplyTheme(theme);

            AppThemeManager.UpdateTitleBarColors();
        }

        private void RbMaterial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            string value = RbMaterial.SelectedIndex switch
            {
                1 => "MicaAlt",
                2 => "Acrylic",
                _ => "Mica"
            };

            localSettings.Values["AppMaterial"] = value;

            AppThemeManager.CurrentMaterial = value switch
            {
                "MicaAlt" => BackgroundMaterial.MicaAlt,
                "Acrylic" => BackgroundMaterial.Acrylic,
                _ => BackgroundMaterial.Mica
            };

            AppThemeManager.ApplyMaterial();
            TextFileViewerWindow.ApplyMaterial();
        }

        private void PanePositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            localSettings.Values["PanePosition"] = PanePositionCombo.SelectedIndex == 1 ? "Top" : "Left";

            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.ApplySettings();
        }

        private void SoundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool isOn = SoundToggle.IsOn;
            localSettings.Values["EnableSound"] = isOn;

            ElementSoundPlayer.State = isOn
                ? ElementSoundPlayerState.On
                : ElementSoundPlayerState.Off;
        }

        private void NotificationToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            localSettings.Values["EnableQueryNotification"] = NotificationToggle.IsOn;
        }

        private async void ViewDisclaimer_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Dialogs.DisclaimerDialog(isManualTrigger: true)
            {
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
