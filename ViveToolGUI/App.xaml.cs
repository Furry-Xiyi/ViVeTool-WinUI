using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;
using Windows.Storage;
using Windows.UI;

namespace ViVeToolGUI
{
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }
        public static MainWindow MainWindowInstance => MainWindow as MainWindow;
        public static string ViVeToolPath { get; private set; }

        private static bool _vivetoolInitialized = false;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var mainInstance = AppInstance.FindOrRegisterForKey("main-instance");

            if (!mainInstance.IsCurrent)
            {
                mainInstance.RedirectActivationToAsync(
                    AppInstance.GetCurrent().GetActivatedEventArgs()
                ).GetAwaiter().GetResult();

                Environment.Exit(0);
                return;
            }

            mainInstance.Activated += (_, _) =>
            {
                MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
                    BringWindowToFront(hwnd);
                });
            };

            AppThemeManager.LoadSettings();

            MainWindow = new MainWindow();

            AppThemeManager.SetupTitleBar();

            MainWindow.Activate();

            _ = InitializeAppAfterSplashAsync();
        }

        private async Task InitializeAppAfterSplashAsync()
        {
            await Task.Delay(50);

            _ = InitializeViVeToolAsync();

            var mw = (MainWindow)MainWindow;

            mw.DispatcherQueue.TryEnqueue(() =>
            {
                try { mw.AppWindow.SetIcon("Assets/AppIcon.ico"); } catch { }

                AppThemeManager.ApplyMaterial();

                var loader = new ResourceLoader();
                mw.AppWindow.Title = loader.GetString("AppDisplayName");

                mw.StartLoadingContent();
            });

            mw.DispatcherQueue.TryEnqueue(async () =>
            {
                await mw.FinishLoadingAndHideSplashAsync();
            });
        }

        private async Task InitializeViVeToolAsync()
        {
            if (_vivetoolInitialized)
                return;

            try
            {
                Debug.WriteLine("[InitViVeTool] Starting initialization...");

                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string vivetoolDir = Path.Combine(programData, "ViVeToolGUI");

                Directory.CreateDirectory(vivetoolDir);

                string arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => "arm64",
                    _ => "x64"
                };

                string targetExe = Path.Combine(vivetoolDir, "ViVeTool.exe");

                Debug.WriteLine($"[InitViVeTool] Copying files from Assets/{arch}...");

                var installFolder = Windows.ApplicationModel.Package.Current.InstalledLocation;
                var assetsFolder = await installFolder.GetFolderAsync("Assets");
                var archFolder = await assetsFolder.GetFolderAsync(arch);
                var files = await archFolder.GetFilesAsync();
                var targetFolder = await StorageFolder.GetFolderFromPathAsync(vivetoolDir);

                foreach (var file in files)
                    await file.CopyAsync(targetFolder, file.Name, NameCollisionOption.ReplaceExisting);

                ViVeToolPath = targetExe;
                _vivetoolInitialized = File.Exists(targetExe);

                Debug.WriteLine($"[InitViVeTool] Path: {ViVeToolPath}");
                Debug.WriteLine($"[InitViVeTool] Initialized: {_vivetoolInitialized}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InitViVeTool] ERROR: {ex}");
            }
        }

        public static async Task<bool> EnsureViVeToolInitializedAsync()
        {
            if (_vivetoolInitialized)
                return true;

            for (int i = 0; i < 50; i++)
            {
                if (_vivetoolInitialized)
                    return true;

                await Task.Delay(100);
            }

            return _vivetoolInitialized;
        }

        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        static void BringWindowToFront(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
            SetForegroundWindow(hwnd);
        }
    }

    public static class AppThemeManager
    {
        public static ElementTheme CurrentTheme = ElementTheme.Default;
        public static BackgroundMaterial CurrentMaterial = BackgroundMaterial.Mica;

        public static void LoadSettings()
        {
            var s = ApplicationData.Current.LocalSettings;

            try
            {
                string theme = s.Values["AppTheme"] as string ?? "System";
                CurrentTheme = theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
            catch { CurrentTheme = ElementTheme.Default; }

            try
            {
                string material = s.Values["AppMaterial"] as string ?? "MicaAlt";
                CurrentMaterial = material switch
                {
                    "MicaAlt" => BackgroundMaterial.MicaAlt,
                    "Acrylic" => BackgroundMaterial.Acrylic,
                    _ => BackgroundMaterial.Mica
                };
            }
            catch { CurrentMaterial = BackgroundMaterial.Mica; }

            try
            {
                bool sound = s.Values["EnableSound"] is bool b ? b : true;
                if (s.Values["EnableSound"] == null)
                    s.Values["EnableSound"] = true;

                ElementSoundPlayer.State = sound
                    ? ElementSoundPlayerState.On
                    : ElementSoundPlayerState.Off;
            }
            catch
            {
                ElementSoundPlayer.State = ElementSoundPlayerState.On;
            }
        }

        public static void ApplyMaterial()
        {
            if (App.MainWindow == null) return;

            try
            {
                if (App.MainWindow.SystemBackdrop is MicaBackdrop mica)
                {
                    if (CurrentMaterial == BackgroundMaterial.Mica && mica.Kind == MicaKind.Base) return;
                    if (CurrentMaterial == BackgroundMaterial.MicaAlt && mica.Kind == MicaKind.BaseAlt) return;
                }
                else if (App.MainWindow.SystemBackdrop is DesktopAcrylicBackdrop &&
                         CurrentMaterial == BackgroundMaterial.Acrylic)
                {
                    return;
                }

                App.MainWindow.SystemBackdrop = CurrentMaterial switch
                {
                    BackgroundMaterial.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                    BackgroundMaterial.Acrylic => new DesktopAcrylicBackdrop(),
                    _ => new MicaBackdrop { Kind = MicaKind.Base }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyMaterial failed: {ex.Message}");
                App.MainWindow.SystemBackdrop = null;
            }
        }

        public static void SetupTitleBar()
        {
            if (App.MainWindow == null) return;

            try
            {
                if (!AppWindowTitleBar.IsCustomizationSupported()) return;

                var titleBar = App.MainWindow.AppWindow.TitleBar;
                titleBar.ExtendsContentIntoTitleBar = true;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

                UpdateTitleBarColors();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetupTitleBar failed: {ex.Message}");
            }
        }

        public static void UpdateTitleBarColors()
        {
            if (App.MainWindow == null) return;

            try
            {
                if (!AppWindowTitleBar.IsCustomizationSupported()) return;

                var titleBar = App.MainWindow.AppWindow.TitleBar;
                bool isDark = GetIsDarkTheme();

                var fg = isDark ? Colors.White : Colors.Black;
                var inactiveFg = isDark
                    ? Color.FromArgb(255, 128, 128, 128)
                    : Color.FromArgb(255, 160, 160, 160);
                var hoverBg = isDark
                    ? Color.FromArgb(20, 255, 255, 255)
                    : Color.FromArgb(20, 0, 0, 0);

                titleBar.ButtonForegroundColor = fg;
                titleBar.ButtonInactiveForegroundColor = inactiveFg;
                titleBar.ButtonHoverBackgroundColor = hoverBg;
                titleBar.ButtonHoverForegroundColor = fg;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(30, hoverBg.R, hoverBg.G, hoverBg.B);
                titleBar.ButtonPressedForegroundColor = fg;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateTitleBarColors failed: {ex.Message}");
            }
        }

        public static void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdateTitleBarColors();
        }

        public static bool GetIsDarkTheme()
        {
            if (App.MainWindow?.Content is FrameworkElement root)
            {
                var actual = root.ActualTheme;
                if (actual != ElementTheme.Default)
                    return actual == ElementTheme.Dark;
            }

            if (CurrentTheme == ElementTheme.Default)
                return Application.Current.RequestedTheme == ApplicationTheme.Dark;

            return CurrentTheme == ElementTheme.Dark;
        }
    }

    public enum BackgroundMaterial
    {
        Mica,
        MicaAlt,
        Acrylic
    }
}
