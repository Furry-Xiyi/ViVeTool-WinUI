using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ViVeToolGUI.Dialogs;
using ViVeToolGUI.Pages;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.System;

namespace ViVeToolGUI
{
    public sealed partial class MainWindow : Window
    {
        private ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
        private static SUBCLASSPROC _subclassProc;
        public static MainWindow Instance { get; private set; }
        public ObservableCollection<string> BreadcrumbItems { get; } = new ObservableCollection<string>();
        private readonly ResourceLoader _loader = new ResourceLoader();
        public static SemaphoreSlim _commandLock = new(1, 1);
        private IntPtr _mainHwnd = IntPtr.Zero;
        private static IntPtr _taskbarPtr = IntPtr.Zero;
        private static bool _taskbarReady = false;
        private static int _taskbarState = 0;
        private static IntPtr _fnSetProgressValue;
        private static IntPtr _fnSetProgressState;
        private static readonly uint WM_TASKBARBUTTONCREATED = RegisterWindowMessage("TaskbarButtonCreated");
        private CancellationTokenSource _taskbarAutoClearCts;
        public bool IsWindowActivated { get; private set; } = true;

        public async void OpenExternalLink(object sender, RoutedEventArgs e)
        {
            var root = (ContentFrame.Content as FrameworkElement)?.XamlRoot;
            if (root == null)
                return;

            string url = "";

            if (sender is Button btn && btn.Tag is string buttonUrl)
                url = buttonUrl;
            else if (sender is HyperlinkButton link && link.Tag is string linkUrl)
                url = linkUrl;

            if (string.IsNullOrWhiteSpace(url))
                return;

            var dialog = new ExternalOpenDialog { XamlRoot = root };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
                await Launcher.LaunchUriAsync(new Uri(url));
        }

        public MainWindow()
        {
            this.InitializeComponent();
            Instance = this;

            if (Content is FrameworkElement root)
                root.RequestedTheme = AppThemeManager.CurrentTheme;

            ContentFrame.Navigated += ContentFrame_Navigated;
            this.Activated += MainWindow_Activated;
            this.Closed += MainWindow_Closed;

            if (Content is FrameworkElement rootEl)
                rootEl.Loaded += Root_Loaded;

            _mainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetMinWindowSize(_mainHwnd, minWidth: 800, minHeight: 520);

            ChangeWindowMessageFilterEx(_mainHwnd, WM_TASKBARBUTTONCREATED, MSGFLT_ALLOW, IntPtr.Zero);
        }

        public void StartLoadingContent()
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            NavigateByTag("enableDisable");
        }

        public void ShowLoadingOverlay(string text = null)
        {
            if (CommonLoadingText != null)
                CommonLoadingText.Text = text ?? "";

            CommonLoadingOverlay.Visibility = Visibility.Visible;
        }

        public void HideLoadingOverlay()
        {
            CommonLoadingOverlay.Visibility = Visibility.Collapsed;
        }

        public void ShowTaskbarIndeterminate()
        {
            CancelTaskbarAutoClear();
            SetProgressIndeterminate();
        }

        public async void ShowTaskbarError(int autoClearMilliseconds = 3500)
        {
            CancelTaskbarAutoClear();
            SetProgressError();

            if (autoClearMilliseconds > 0)
            {
                _taskbarAutoClearCts = new CancellationTokenSource();
                var token = _taskbarAutoClearCts.Token;

                try
                {
                    await Task.Delay(autoClearMilliseconds, token);
                    if (!token.IsCancellationRequested)
                        ClearProgress();
                }
                catch (TaskCanceledException) { }
            }
        }

        public async void ShowTaskbarCompleted(int autoClearMilliseconds = 1200)
        {
            CancelTaskbarAutoClear();
            SetProgressCompleted();
            FlashTaskbarButton();

            if (autoClearMilliseconds > 0)
            {
                _taskbarAutoClearCts = new CancellationTokenSource();
                var token = _taskbarAutoClearCts.Token;

                try
                {
                    await Task.Delay(autoClearMilliseconds, token);
                    if (!token.IsCancellationRequested)
                        ClearProgress();
                }
                catch (TaskCanceledException) { }
            }
        }

        public void ClearTaskbarProgress()
        {
            CancelTaskbarAutoClear();
            ClearProgress();
        }

        private void CancelTaskbarAutoClear()
        {
            try
            {
                _taskbarAutoClearCts?.Cancel();
                _taskbarAutoClearCts?.Dispose();
            }
            catch { }
            _taskbarAutoClearCts = null;
        }

        private void FlashTaskbarButton()
        {
            try
            {
                if (_mainHwnd == IntPtr.Zero)
                    _mainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

                if (_mainHwnd == IntPtr.Zero)
                    return;

                FLASHWINFO info = new FLASHWINFO
                {
                    cbSize = Convert.ToUInt32(Marshal.SizeOf<FLASHWINFO>()),
                    hwnd = _mainHwnd,
                    dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
                    uCount = 3,
                    dwTimeout = 0
                };

                FlashWindowEx(ref info);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FlashTaskbarButton Error: {ex.Message}");
            }
        }

        private static Type TagToPageType(string tag)
        {
            return tag switch
            {
                "enableDisable" => typeof(EnableDisablePage),
                "query" => typeof(QueryPage),
                "reset" => typeof(ResetPage),
                "settings" => typeof(SettingsPage),
                _ => null
            };
        }

        private void NavigateByTag(string tag, Microsoft.UI.Xaml.Media.Animation.NavigationTransitionInfo transitionInfo = null)
        {
            var pageType = TagToPageType(tag);
            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType, null, transitionInfo ?? new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo());
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                    ContentFrame.Navigate(typeof(SettingsPage), null, args.RecommendedNavigationTransitionInfo);
                return;
            }
            string tag = args.InvokedItemContainer?.Tag?.ToString();
            NavigateByTag(tag, args.RecommendedNavigationTransitionInfo);
        }
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            UpdateBackButton();
            UpdateSelectedNavItem(e.SourcePageType);
            UpdateBreadcrumb(e.SourcePageType);
            UpdateTitleBarMargin();
        }

        private void UpdateBackButton()
        {
            BackButton.Visibility = ContentFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateTitleBarMargin()
        {
            var targetMargin = ContentFrame.CanGoBack
                ? new Thickness(34, 0, 0, 0)
                : new Thickness(2, 0, 0, 0);

            if (!TitleBarArea.Margin.Equals(targetMargin))
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    TitleBarArea.Margin = targetMargin;
                });
            }
        }

        private void UpdateSelectedNavItem(Type pageType)
        {
            if (pageType == typeof(SettingsPage))
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
            {
                string tag = item.Tag?.ToString();
                if (TagToPageType(tag) == pageType)
                {
                    NavView.SelectedItem = item;
                    return;
                }
            }
        }

        private void UpdateBreadcrumb(Type pageType)
        {
            if (pageType == null) return;
            BreadcrumbItems.Clear();

            string position = localSettings.Values["PanePosition"] as string ?? "Left";

            // 【新增需求】：顶部模式下，除了设置页外的其他页面全部隐藏面包屑栏
            if (position == "Top" && pageType != typeof(SettingsPage))
            {
                BreadcrumbPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (pageType == typeof(EnableDisablePage))
                BreadcrumbItems.Add(_loader.GetString("EnableDisable_Breadcrumb"));
            else if (pageType == typeof(QueryPage))
                BreadcrumbItems.Add(_loader.GetString("Query_Breadcrumb"));
            else if (pageType == typeof(ResetPage))
                BreadcrumbItems.Add(_loader.GetString("Reset_Breadcrumb"));
            else if (pageType == typeof(SettingsPage))
                BreadcrumbItems.Add(_loader.GetString("Settings_Breadcrumb"));

            BreadcrumbPanel.Visibility = BreadcrumbItems.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            bool isActive = args.WindowActivationState != WindowActivationState.Deactivated;
            IsWindowActivated = isActive;
            if (isActive)
                ClearBadge();
        }

        public void SetBadge(int count)
        {
            try
            {
                string xml = $"<badge value=\"{Math.Clamp(count, 1, 99)}\"/>";
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(xml);
                Windows.UI.Notifications.BadgeUpdateManager
                    .CreateBadgeUpdaterForApplication()
                    .Update(new Windows.UI.Notifications.BadgeNotification(doc));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetBadge Error: {ex.Message}");
            }
        }

        public void ClearBadge()
        {
            try
            {
                Windows.UI.Notifications.BadgeUpdateManager
                    .CreateBadgeUpdaterForApplication()
                    .Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ClearBadge Error: {ex.Message}");
            }
        }

        public void ShowNotification(string title, string body)
        {
            try
            {
                var notifier = Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier();

                string toastXml = $@"
<toast>
    <visual>
        <binding template='ToastGeneric'>
            <text>{System.Security.SecurityElement.Escape(title)}</text>
            <text>{System.Security.SecurityElement.Escape(body)}</text>
        </binding>
    </visual>
</toast>";

                var xmlDoc = new Windows.Data.Xml.Dom.XmlDocument();
                xmlDoc.LoadXml(toastXml);

                var toast = new Windows.UI.Notifications.ToastNotification(xmlDoc);
                notifier.Show(toast);

                Debug.WriteLine($"[Notification] Sent: {title} - {body}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowNotification Error: {ex.Message}");
            }
        }

        public async Task FinishLoadingAndHideSplashAsync()
        {
            await Task.Delay(500);

            SplashFadeOut.Completed += async (s, e) =>
            {
                SplashOverlay.Visibility = Visibility.Collapsed;

                bool sound = localSettings.Values["EnableSound"] is bool b ? b : true;
                ElementSoundPlayer.State = sound
                    ? ElementSoundPlayerState.On
                    : ElementSoundPlayerState.Off;

                bool accepted = localSettings.Values["DisclaimerAccepted"] is bool v && v;
                if (!accepted)
                {
                    var dialog = new Dialogs.DisclaimerDialog();
                    dialog.XamlRoot = this.Content.XamlRoot;
                    var result = await dialog.ShowAsync();

                    if (result != ContentDialogResult.Primary)
                    {
                        Application.Current.Exit();
                    }
                }
            };

            SplashFadeOut.Begin();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            try { ClearTaskbarProgress(); } catch { }
            try
            {
                string tempDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ViVeToolGUI",
                    "Temp");

                if (Directory.Exists(tempDir))
                {
                    foreach (var file in Directory.GetFiles(tempDir, "vivetool_*.txt"))
                        try { File.Delete(file); } catch { }

                    foreach (var file in Directory.GetFiles(tempDir, "vivetool_*.bat"))
                        try { File.Delete(file); } catch { }
                }
            }
            catch { }

            Application.Current.Exit();
            Environment.Exit(0);
        }

        public static async Task<CommandResult> ExecuteViVeToolCommandAsync(
            string arguments, Func<Task<bool>>? consoleConfirmation = null)
        {
            await _commandLock.WaitAsync();

            try
            {
                bool requiresConsoleConfirmation = string.Equals(
                    arguments.TrimStart().Split(' ', 2)[0], "/fullreset", StringComparison.OrdinalIgnoreCase);
                if (requiresConsoleConfirmation && consoleConfirmation == null)
                {
                    return new CommandResult
                    {
                        ExitCode = -1,
                        Error = "The full reset command requires a confirmation callback."
                    };
                }

                if (!await App.EnsureViVeToolInitializedAsync())
                {
                    return new CommandResult { ExitCode = -1, Output = "", Error = "ViVeTool initialization failed." };
                }

                if (string.IsNullOrEmpty(App.ViVeToolPath) || !File.Exists(App.ViVeToolPath))
                {
                    Debug.WriteLine($"[ExecuteViVeTool] ERROR: ViVeTool.exe not found at: {App.ViVeToolPath}");
                    return new CommandResult { ExitCode = -1, Output = "", Error = "ViVeTool.exe not found. Please restart the application." };
                }

                string vivetoolDir = Path.GetDirectoryName(App.ViVeToolPath);
                string viveDll = Path.Combine(vivetoolDir, "vive.dll");

                if (!File.Exists(viveDll))
                    Debug.WriteLine($"[ExecuteViVeTool] WARNING: vive.dll not found at: {viveDll}");

                Debug.WriteLine($"[ExecuteViVeTool] ViVeToolPath: {App.ViVeToolPath}");
                Debug.WriteLine($"[ExecuteViVeTool] Arguments: {arguments}");

                string tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ViVeToolGUI", "Temp");
                Directory.CreateDirectory(tempDir);

                string outputFile = Path.Combine(tempDir, $"vivetool_{Guid.NewGuid():N}.txt");
                string batchFile = Path.Combine(tempDir, $"vivetool_{Guid.NewGuid():N}.bat");

                string batchContent = $@"@echo off
cd /d ""{vivetoolDir}""
""{App.ViVeToolPath}"" {arguments} > ""{outputFile}"" 2>&1
echo EXIT_CODE=%ERRORLEVEL% >> ""{outputFile}""
";
                await File.WriteAllTextAsync(batchFile, batchContent, Encoding.UTF8);

                var psi = new ProcessStartInfo
                {
                    FileName = batchFile,
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = vivetoolDir
                };

                int exitCode = 0;

                try
                {
                    using var proc = Process.Start(psi);
                    if (proc == null)
                    {
                        try { File.Delete(batchFile); } catch { }
                        return new CommandResult { ExitCode = -1, Output = "", Error = "Failed to start elevated process." };
                    }

                    if (requiresConsoleConfirmation)
                    {
                        try
                        {
                            bool confirmed = await consoleConfirmation!();
                            await Task.Run(() => SendConsoleKeyToProcess(proc.Id, confirmed ? 'Y' : 'N'));
                        }
                        catch (Exception ex)
                        {
                            try { proc.Kill(entireProcessTree: true); } catch { }
                            try { File.Delete(batchFile); } catch { }
                            try { File.Delete(outputFile); } catch { }
                            throw new InvalidOperationException(
                                $"Could not send the reset confirmation to ViVeTool: {ex.Message}", ex);
                        }
                    }

                    await Task.Run(() =>
                    {
                        proc.WaitForExit();
                        exitCode = proc.ExitCode;
                    });
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    Debug.WriteLine($"[ExecuteViVeTool] User cancelled UAC: {ex.Message}");
                    try { File.Delete(batchFile); } catch { }
                    return new CommandResult { ExitCode = -1, Output = "", Error = "User cancelled UAC elevation." };
                }

                await Task.Delay(1000);
                string output = "";

                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        if (File.Exists(outputFile))
                        {
                            output = await File.ReadAllTextAsync(outputFile, Encoding.UTF8);
                            if (output.Length > 0)
                            {
                                var exitCodeMatch = System.Text.RegularExpressions.Regex.Match(output, @"EXIT_CODE=(-?\d+)");
                                if (exitCodeMatch.Success)
                                {
                                    exitCode = int.Parse(exitCodeMatch.Groups[1].Value);
                                    output = output.Replace(exitCodeMatch.Value, "").Trim();
                                }
                            }
                            try { File.Delete(outputFile); } catch { }
                            break;
                        }
                        await Task.Delay(500);
                    }
                    catch
                    {
                        await Task.Delay(500);
                    }
                }

                try { File.Delete(batchFile); } catch { }

                return new CommandResult
                {
                    ExitCode = exitCode,
                    Output = output,
                    Error = exitCode == 0 ? "" : $"ViVeTool exited with code {exitCode}."
                };
            }
            finally
            {
                _commandLock.Release();
            }
        }

        private static void SendConsoleKeyToProcess(int processId, char character)
        {
            int lastError = 0;
            bool attached = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                FreeConsole();
                if (AttachConsole((uint)processId))
                {
                    attached = true;
                    break;
                }

                lastError = Marshal.GetLastWin32Error();
                Thread.Sleep(50);
            }

            if (!attached)
                throw new System.ComponentModel.Win32Exception(lastError);

            try
            {
                IntPtr inputHandle = CreateFileW(
                    "CONIN$",
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);
                if (inputHandle == INVALID_HANDLE_VALUE)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                try
                {
                    ushort virtualKey = character;
                    var keyDown = new KeyEventRecord
                    {
                        KeyDown = true,
                        RepeatCount = 1,
                        VirtualKeyCode = virtualKey,
                        UnicodeChar = character
                    };
                    var keyUp = keyDown;
                    keyUp.KeyDown = false;

                    var input = new[]
                    {
                        new InputRecord { EventType = KEY_EVENT, KeyEvent = keyDown },
                        new InputRecord { EventType = KEY_EVENT, KeyEvent = keyUp }
                    };

                    if (!WriteConsoleInputW(inputHandle, input, (uint)input.Length, out uint written) || written != input.Length)
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
                finally
                {
                    CloseHandle(inputHandle);
                }
            }
            finally
            {
                FreeConsole();
            }
        }

        static int _minW, _minH;

        static void SetMinWindowSize(IntPtr hwnd, int minWidth, int minHeight)
        {
            _minW = minWidth;
            _minH = minHeight;
            _subclassProc = SubclassProc;
            SetWindowSubclass(hwnd, _subclassProc, 0, 0);
        }

        public unsafe void SetProgress(int percent)
        {
            if (!_taskbarReady) return;
            if (_taskbarState != 2)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)_fnSetProgressState)(_taskbarPtr, _mainHwnd, 2);
                _taskbarState = 2;
            }
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ulong, ulong, int>)_fnSetProgressValue)(_taskbarPtr, _mainHwnd, (ulong)Math.Clamp(percent, 0, 100), 100UL);
        }

        public unsafe void SetProgressIndeterminate()
        {
            if (!_taskbarReady || _taskbarState == 1) return;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)_fnSetProgressState)(_taskbarPtr, _mainHwnd, 1);
            _taskbarState = 1;
            Debug.WriteLine("[Taskbar] State = Indeterminate");
        }

        public unsafe void ClearProgress()
        {
            if (!_taskbarReady || _taskbarState == 0) return;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)_fnSetProgressState)(_taskbarPtr, _mainHwnd, 0);
            _taskbarState = 0;
        }

        public unsafe void SetProgressError()
        {
            if (!_taskbarReady) return;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)_fnSetProgressState)(_taskbarPtr, _mainHwnd, 4);
            _taskbarState = 4;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ulong, ulong, int>)_fnSetProgressValue)(_taskbarPtr, _mainHwnd, 100UL, 100UL);
        }

        public unsafe void SetProgressCompleted()
        {
            if (!_taskbarReady) return;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)_fnSetProgressState)(_taskbarPtr, _mainHwnd, 2);
            _taskbarState = 2;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ulong, ulong, int>)_fnSetProgressValue)(_taskbarPtr, _mainHwnd, 100UL, 100UL);
        }

        private static unsafe void OnTaskbarButtonCreated()
        {
#pragma warning disable IL2026
            if (_taskbarReady) return;

            var clsid = new Guid(0x56fdf344, 0xfd6d, 0x11d0, 0x95, 0x8a, 0x00, 0x60, 0x97, 0xc9, 0xa0, 0x90);
            var iid = new Guid(0xea1afb91, 0x9e28, 0x4b86, 0x90, 0xe9, 0x9e, 0x9f, 0x8a, 0x5e, 0xef, 0xaf);

            if (CoCreateInstance(ref clsid, IntPtr.Zero, 1u, ref iid, out var ptr) != 0 || ptr == IntPtr.Zero)
                return;

            void** vtbl = *(void***)ptr.ToPointer();

            var hrInit = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtbl[3];
            if (hrInit(ptr) != 0)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vtbl[2])(ptr);
                return;
            }

            _fnSetProgressValue = (IntPtr)vtbl[9];
            _fnSetProgressState = (IntPtr)vtbl[10];

            _taskbarPtr = ptr;
            _taskbarReady = true;

            Debug.WriteLine("[Taskbar] ITaskbarList3 initialized via unsafe vtable.");
#pragma warning restore IL2026
        }

        static nuint SubclassProc(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
        {
            if (uMsg == 0x0024)
            {
                double dpi = GetDpiForWindow(hWnd) / 96.0;
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize.x = (int)(_minW * dpi);
                info.ptMinTrackSize.y = (int)(_minH * dpi);
                Marshal.StructureToPtr(info, lParam, true);
            }

            if (uMsg == WM_TASKBARBUTTONCREATED)
            {
                OnTaskbarButtonCreated();
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        delegate nuint SUBCLASSPROC(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData);

        [DllImport("comctl32.dll")]
        static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

        [DllImport("ole32.dll")]
        static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

        [DllImport("comctl32.dll")]
        static extern nuint DefSubclassProc(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const ushort KEY_EVENT = 0x0001;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        [StructLayout(LayoutKind.Explicit)]
        private struct InputRecord
        {
            [FieldOffset(0)]
            public ushort EventType;

            [FieldOffset(4)]
            public KeyEventRecord KeyEvent;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyEventRecord
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool KeyDown;
            public ushort RepeatCount;
            public ushort VirtualKeyCode;
            public ushort VirtualScanCode;
            public char UnicodeChar;
            public uint ControlKeyState;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", EntryPoint = "WriteConsoleInputW", SetLastError = true)]
        private static extern bool WriteConsoleInputW(
            IntPtr hConsoleInput,
            [In] InputRecord[] lpBuffer,
            uint nLength,
            out uint lpNumberOfEventsWritten);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO
        {
            public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int x, y;
        }

        private const uint FLASHW_TRAY = 0x00000002;
        private const uint FLASHW_TIMERNOFG = 0x0000000C;

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr pChangeFilterStruct);

        private const uint MSGFLT_ALLOW = 1;

        private void Root_Loaded(object sender, RoutedEventArgs e)
        {
            AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
            TitleBarArea.Title = Package.Current.DisplayName;
            TitleBarArea.Icon = new ImageIcon { Source = new BitmapImage(Package.Current.Logo) };
            ApplySettings();
            UpdateBackButton();
            UpdateTitleBarMargin();
            if (Content is FrameworkElement root)
            {
                root.ActualThemeChanged -= AppThemeManager.OnActualThemeChanged;
                root.ActualThemeChanged += AppThemeManager.OnActualThemeChanged;
                AppThemeManager.UpdateTitleBarColors();
            }
        }

        public void ApplySettings()
        {
            try
            {
                string position = localSettings.Values["PanePosition"] as string ?? "Left";
                if (localSettings.Values["PanePosition"] == null)
                    localSettings.Values["PanePosition"] = "Left";

                if (position == "Top")
                {
                    NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Top;
                    NavView.IsPaneToggleButtonVisible = false;
                    NavViewContainer.Margin = new Thickness(0, 48, 0, 0);
                }
                else
                {
                    NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
                    NavView.IsPaneToggleButtonVisible = false;
                    NavViewContainer.Margin = new Thickness(0, 48, 0, 0);
                }

                // 切换顶部/左侧模式后主动刷新一次面包屑判断逻辑
                if (ContentFrame.CurrentSourcePageType != null)
                {
                    UpdateBreadcrumb(ContentFrame.CurrentSourcePageType);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplySettings Error: {ex.Message}");
            }
        }
    }

    public class CommandResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
    }
}
