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

        private static readonly object _viVeToolInitializationLock = new();
        private static Task<bool>? _viVeToolInitializationTask;

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

            MainWindow.Activate();

            _ = InitializeAppAfterSplashAsync();
        }

        private async Task InitializeAppAfterSplashAsync()
        {
            await Task.Delay(50);

            _ = EnsureViVeToolInitializedAsync();

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

        private static Task<bool> GetViVeToolInitializationTask()
        {
            lock (_viVeToolInitializationLock)
                return _viVeToolInitializationTask ??= InitializeViVeToolAsync();
        }

        private static async Task<bool> InitializeViVeToolAsync()
        {
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
                bool initialized = File.Exists(targetExe);

                Debug.WriteLine($"[InitViVeTool] Path: {ViVeToolPath}");
                Debug.WriteLine($"[InitViVeTool] Initialized: {initialized}");
                return initialized;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InitViVeTool] ERROR: {ex}");
                return false;
            }
        }

        public static async Task<bool> EnsureViVeToolInitializedAsync()
        {
            return await GetViVeToolInitializationTask();
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
                var backdrop = CreateSystemBackdrop(CurrentMaterial);

                if (App.MainWindow.SystemBackdrop is MicaBackdrop mica)
                {
                    if (CurrentMaterial == BackgroundMaterial.Mica && mica.Kind == MicaKind.Base) return;
                    if (CurrentMaterial == BackgroundMaterial.MicaAlt && mica.Kind == MicaKind.BaseAlt) return;
                }
                else if (App.MainWindow.SystemBackdrop is DesktopAcrylicBackdrop && backdrop is DesktopAcrylicBackdrop)
                {
                    return;
                }

                App.MainWindow.SystemBackdrop = backdrop;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyMaterial failed: {ex.Message}");
                App.MainWindow.SystemBackdrop = null;
            }
        }

        public static SystemBackdrop? CreateSystemBackdrop(BackgroundMaterial material)
        {
            return material switch
            {
                BackgroundMaterial.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                // WinUI Gallery's built-in Desktop Acrylic backdrop uses the Base kind.
                BackgroundMaterial.Acrylic => new DesktopAcrylicBackdrop(),
                _ => new MicaBackdrop { Kind = MicaKind.Base }
            };
        }

        public static void UpdateTitleBarColors()
        {
            if (App.MainWindow == null) return;

            try
            {
                if (!AppWindowTitleBar.IsCustomizationSupported()) return;

                UpdateTitleBarColors(App.MainWindow.AppWindow.TitleBar, GetIsDarkTheme());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateTitleBarColors failed: {ex.Message}");
            }
        }

        public static void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdateTitleBarColors(App.MainWindow?.AppWindow.TitleBar, sender.ActualTheme == ElementTheme.Dark);
        }

        public static void UpdateTitleBarColors(AppWindowTitleBar? titleBar, bool isDark)
        {
            if (titleBar == null || !AppWindowTitleBar.IsCustomizationSupported()) return;

            var foreground = isDark ? Colors.White : Colors.Black;
            var hoverBackground = isDark
                ? Color.FromArgb(24, 255, 255, 255)
                : Color.FromArgb(24, 0, 0, 0);

            // Match WinUI Gallery: leave default, inactive, and pressed states to Windows.
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = hoverBackground;
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
