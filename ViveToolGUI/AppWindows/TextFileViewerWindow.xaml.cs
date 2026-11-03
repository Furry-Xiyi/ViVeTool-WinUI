using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Tabular;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System.ComponentModel;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Globalization;
using Windows.Graphics;
using Windows.System;
using Windows.System.UserProfile;
using Windows.UI;
using Windows.Foundation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Animation;

namespace ViVeToolGUI.AppWindows
{
    public sealed partial class FeatureEntry : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Variant { get; set; } = "";
        public string Description { get; set; } = "";

        private string _query = "";
        public string Query { get => _query; set { _query = value; OnChanged(nameof(Query)); } }

        private bool _caseSensitive;
        public bool CaseSensitive { get => _caseSensitive; set { _caseSensitive = value; OnChanged(nameof(CaseSensitive)); } }

        private int _currentField;
        public int CurrentField
        {
            get => _currentField;
            set
            {
                if (_currentField == value) return;
                _currentField = value;
                OnChanged(nameof(IdIsCurrent));
                OnChanged(nameof(VariantIsCurrent));
                OnChanged(nameof(DescIsCurrent));
            }
        }

        private int _currentMatchPos = -1;
        public int CurrentMatchPos
        {
            get => _currentMatchPos;
            set { if (_currentMatchPos == value) return; _currentMatchPos = value; OnChanged(nameof(CurrentMatchPos)); }
        }

        public bool IdIsCurrent => _currentField == 1;
        public bool VariantIsCurrent => _currentField == 2;
        public bool DescIsCurrent => _currentField == 3;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal static class SearchHighlight
    {
        public static readonly DependencyProperty SourceTextProperty =
            DependencyProperty.RegisterAttached("SourceText", typeof(string), typeof(SearchHighlight), new PropertyMetadata(null, OnPropChanged));
        public static string GetSourceText(DependencyObject o) => (string)o.GetValue(SourceTextProperty);
        public static void SetSourceText(DependencyObject o, string v) => o.SetValue(SourceTextProperty, v);

        public static readonly DependencyProperty QueryProperty =
            DependencyProperty.RegisterAttached("Query", typeof(string), typeof(SearchHighlight), new PropertyMetadata("", OnPropChanged));
        public static string GetQuery(DependencyObject o) => (string)o.GetValue(QueryProperty);
        public static void SetQuery(DependencyObject o, string v) => o.SetValue(QueryProperty, v);

        public static readonly DependencyProperty CaseSensitiveProperty =
            DependencyProperty.RegisterAttached("CaseSensitive", typeof(bool), typeof(SearchHighlight), new PropertyMetadata(false, OnPropChanged));
        public static bool GetCaseSensitive(DependencyObject o) => (bool)o.GetValue(CaseSensitiveProperty);
        public static void SetCaseSensitive(DependencyObject o, bool v) => o.SetValue(CaseSensitiveProperty, v);

        public static readonly DependencyProperty IsCurrentFieldProperty =
            DependencyProperty.RegisterAttached("IsCurrentField", typeof(bool), typeof(SearchHighlight), new PropertyMetadata(false, OnPropChanged));
        public static bool GetIsCurrentField(DependencyObject o) => (bool)o.GetValue(IsCurrentFieldProperty);
        public static void SetIsCurrentField(DependencyObject o, bool v) => o.SetValue(IsCurrentFieldProperty, v);

        public static readonly DependencyProperty CurrentMatchPosProperty =
            DependencyProperty.RegisterAttached("CurrentMatchPos", typeof(int), typeof(SearchHighlight), new PropertyMetadata(-1, OnPropChanged));
        public static int GetCurrentMatchPos(DependencyObject o) => (int)o.GetValue(CurrentMatchPosProperty);
        public static void SetCurrentMatchPos(DependencyObject o, int v) => o.SetValue(CurrentMatchPosProperty, v);

        private static void OnPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock tb) Apply(tb);
        }

        private static void Apply(TextBlock tb)
        {
            string text = GetSourceText(tb) ?? "";
            tb.Text = text;
            tb.TextHighlighters.Clear();

            string query = GetQuery(tb);
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) return;

            var cmp = GetCaseSensitive(tb) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            bool isCurrentField = GetIsCurrentField(tb);
            int currentPos = GetCurrentMatchPos(tb);

            var defaultHl = new TextHighlighter
            {
                Background = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]),
                Foreground = new SolidColorBrush(Colors.White)
            };
            var currentHl = new TextHighlighter
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 255, 185, 0)),
                Foreground = new SolidColorBrush(Colors.Black)
            };

            int pos = 0;
            while ((pos = text.IndexOf(query, pos, cmp)) >= 0)
            {
                if (isCurrentField && pos == currentPos)
                    currentHl.Ranges.Add(new TextRange(pos, query.Length));
                else
                    defaultHl.Ranges.Add(new TextRange(pos, query.Length));
                pos += query.Length;
            }
            if (defaultHl.Ranges.Count > 0)
                tb.TextHighlighters.Add(defaultHl);
            if (currentHl.Ranges.Count > 0)
                tb.TextHighlighters.Add(currentHl);
        }
    }

    public sealed partial class TextFileViewerWindow : Window
    {
        private static TextFileViewerWindow? _instance;
        private AppWindow _appWindow;
        private Windows.System.DispatcherQueueTimer _searchTimer;
        private List<FeatureEntry> _allItems = new();
        private List<(int Index, int Field, int Pos)> _matches = new();
        private int _prevCurrentIndex = -1;
        private int _currentMatchIndex = -1;
        private bool _searchPending;
        private int _bringIntoViewVersion;
        public ObservableCollection<FeatureEntry> FilteredItems { get; } = new();
        private const int GWLP_WNDPROC = -4;
        private const uint WM_NCLBUTTONDBLCLK = 0x00A3;
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private WndProcDelegate? _newWndProc;
        private IntPtr _oldWndProc;
        private IntPtr _hwnd;

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
        private CancellationTokenSource? _fetchCts;
        private Windows.System.DispatcherQueueTimer? _infoBarDismissTimer;
        private Storyboard? _spinStoryboard;
        private bool _isSpinning;
        private string _ctxIdCsv = "";
        private string _ctxVariantOptions = "";
        private string _ctxCopyContent = "";
        private TextBlock? _ctxCopySourceTb;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtrRaw(IntPtr hWnd, int nIndex, IntPtr newProc);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private void DisableDoubleClickMaximize()
        {
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _newWndProc = WndProc;
            _oldWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _newWndProc);
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_NCLBUTTONDBLCLK)
                return IntPtr.Zero;
            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        public static void ShowOrActivate()
        {
            if (_instance != null)
            {
                _instance._appWindow.Show(true);
                return;
            }
            _instance = new TextFileViewerWindow();
            _instance.Activate();
        }

        public TextFileViewerWindow()
        {
            this.InitializeComponent();

            _searchTimer = Windows.System.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(300);
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); ApplySearch(); };

            _infoBarDismissTimer = Windows.System.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _infoBarDismissTimer.IsRepeating = false;
            _infoBarDismissTimer.Tick += (s, e) => { _infoBarDismissTimer.Stop(); HideInfoBar(); };

            _appWindow = this.AppWindow;
            _appWindow.SetIcon("Assets\\AppIcon.ico");
            _appWindow.Title = new ResourceLoader().GetString("TextFileViewerWindowTitle");
            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

            var presenter = OverlappedPresenter.Create();
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = true;
            presenter.PreferredMinimumWidth = 900;
            presenter.PreferredMinimumHeight = 500;
            _appWindow.SetPresenter(presenter);
            DisableDoubleClickMaximize();

            this.SystemBackdrop = AppThemeManager.CreateSystemBackdrop(AppThemeManager.CurrentMaterial);

            _appWindow.Changed += AppWindow_Changed;
            this.Closed += TextFileViewerWindow_Closed;

            LoadCsv();
            _ = FetchRemoteCsvAsync();

        }

        private void TextFileViewerWindow_Closed(object sender, WindowEventArgs args)
        {
            _instance = null;
            if (_appWindow != null)
                _appWindow.Changed -= AppWindow_Changed;
            _searchTimer?.Stop();
            _bringIntoViewVersion++;
            _infoBarDismissTimer?.Stop();
            _fetchCts?.Cancel();
            _isSpinning = false;
            _spinStoryboard?.Stop();
            _spinStoryboard = null;
            if (_oldWndProc != IntPtr.Zero && _hwnd != IntPtr.Zero)
            {
                SetWindowLongPtrRaw(_hwnd, GWLP_WNDPROC, _oldWndProc);
                _oldWndProc = IntPtr.Zero;
            }
            _newWndProc = null;
            this.SystemBackdrop = null;
        }

        public static void ApplyTheme(ElementTheme theme)
        {
            if (_instance?.Content is FrameworkElement root)
                root.RequestedTheme = theme;
        }

        public static void ApplyMaterial()
        {
            if (_instance != null)
                _instance.SystemBackdrop = AppThemeManager.CreateSystemBackdrop(AppThemeManager.CurrentMaterial);
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidSizeChange)
                UpdateDragRects();
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (Content is FrameworkElement root)
            {
                root.RequestedTheme = AppThemeManager.CurrentTheme;
                root.ActualThemeChanged -= RootGrid_ActualThemeChanged;
                root.ActualThemeChanged += RootGrid_ActualThemeChanged;
                AppThemeManager.UpdateTitleBarColors(_appWindow.TitleBar, root.ActualTheme == ElementTheme.Dark);
            }

            UpdateDragRects();
        }

        private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args)
        {
            AppThemeManager.UpdateTitleBarColors(_appWindow.TitleBar, sender.ActualTheme == ElementTheme.Dark);
        }

        private void UpdateDragRects()
        {
            if (_appWindow == null || Content?.XamlRoot == null || ControlContainer == null || !ControlContainer.IsLoaded) return;
            try
            {
                var scale = Content.XamlRoot.RasterizationScale;
                var titleBarHeight = (int)(_appWindow.TitleBar.Height * scale);
                var windowWidth = _appWindow.Size.Width;

                int left, right;
                if (_controlsExpanded)
                {
                    var containerPos = ControlContainer.TransformToVisual(Content).TransformPoint(new Windows.Foundation.Point(0, 0));
                    left = (int)(containerPos.X * scale);
                    double containerRight = containerPos.X + ControlContainer.ActualWidth;
                    if (RefreshButton.Visibility == Visibility.Visible)
                    {
                        var refreshPos = RefreshButton.TransformToVisual(Content).TransformPoint(new Windows.Foundation.Point(0, 0));
                        containerRight = refreshPos.X + RefreshButton.ActualWidth;
                    }
                    right = (int)(containerRight * scale);
                }
                else
                {
                    var searchPos = SearchBox.TransformToVisual(Content).TransformPoint(new Windows.Foundation.Point(0, 0));
                    left = (int)(searchPos.X * scale);
                    right = (int)((searchPos.X + SearchBox.ActualWidth) * scale);
                }

                var rects = new List<RectInt32>();
                if (left > 0)
                    rects.Add(new RectInt32(0, 0, left, titleBarHeight));
                if (right < windowWidth)
                    rects.Add(new RectInt32(right, 0, windowWidth - right, titleBarHeight));

                _appWindow.TitleBar.SetDragRectangles(rects.ToArray());
            }
            catch { }
        }

        private bool _controlsExpanded;

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _controlsExpanded = true;
            MatchCaseToggle.IsHitTestVisible = true;
            NavButtonsPanel.IsHitTestVisible = true;
            ShowControlsStoryboard.Begin();
            ShowControlsStoryboard.Completed -= ShowControls_Completed;
            ShowControlsStoryboard.Completed += ShowControls_Completed;
        }

        private void ShowControls_Completed(object? sender, object e)
        {
            ShowControlsStoryboard.Completed -= ShowControls_Completed;
            UpdateDragRects();
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_instance == null) return;
            if (string.IsNullOrEmpty(SearchBox.Text))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_instance == null) return;
                    var focused = FocusManager.GetFocusedElement(Content.XamlRoot) as DependencyObject;
                    if (IsInVisualTree(focused, ControlContainer))
                        return;
                    _controlsExpanded = false;
                    MatchCaseToggle.IsHitTestVisible = false;
                    NavButtonsPanel.IsHitTestVisible = false;
                    HideControlsStoryboard.Begin();
                    HideControlsStoryboard.Completed -= HideControls_Completed;
                    HideControlsStoryboard.Completed += HideControls_Completed;
                });
            }
        }

        private static bool IsInVisualTree(DependencyObject? child, DependencyObject parent)
        {
            while (child != null)
            {
                if (child == parent) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        private void HideControls_Completed(object? sender, object e)
        {
            HideControlsStoryboard.Completed -= HideControls_Completed;
            UpdateDragRects();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchPending = true;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void MatchCaseToggle_Click(object sender, RoutedEventArgs e) => ApplySearch();

        private void ApplySearch()
        {
            _searchPending = false;
            string query = SearchBox.Text.Trim();
            bool caseSensitive = MatchCaseToggle.IsChecked == true;

            if (_prevCurrentIndex >= 0 && _prevCurrentIndex < FilteredItems.Count)
            {
                FilteredItems[_prevCurrentIndex].CurrentField = 0;
                FilteredItems[_prevCurrentIndex].CurrentMatchPos = -1;
            }
            _prevCurrentIndex = -1;

            _matches.Clear();
            _currentMatchIndex = -1;

            if (string.IsNullOrEmpty(query))
            {
                foreach (var item in FilteredItems)
                {
                    item.Query = "";
                    item.CaseSensitive = caseSensitive;
                }
                MatchCountText.Text = "";
                return;
            }

            var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            for (int i = 0; i < FilteredItems.Count; i++)
            {
                var item = FilteredItems[i];
                item.Query = query;
                item.CaseSensitive = caseSensitive;

                int pos = 0;
                while ((pos = item.Id.IndexOf(query, pos, cmp)) >= 0) { _matches.Add((i, 1, pos)); pos += query.Length; }
                pos = 0;
                while ((pos = item.Variant.IndexOf(query, pos, cmp)) >= 0) { _matches.Add((i, 2, pos)); pos += query.Length; }
                pos = 0;
                while ((pos = item.Description.IndexOf(query, pos, cmp)) >= 0) { _matches.Add((i, 3, pos)); pos += query.Length; }
            }

            if (_matches.Count > 0)
            {
                _currentMatchIndex = 0;
                HighlightCurrentMatch();
                MatchCountText.Text = $"1/{_matches.Count}";
            }
            else
            {
                MatchCountText.Text = "0/0";
            }
        }

        private void HighlightCurrentMatch()
        {
            if (_prevCurrentIndex >= 0 && _prevCurrentIndex < FilteredItems.Count)
            {
                FilteredItems[_prevCurrentIndex].CurrentField = 0;
                FilteredItems[_prevCurrentIndex].CurrentMatchPos = -1;
            }

            var (idx, field, pos) = _matches[_currentMatchIndex];
            FilteredItems[idx].CurrentField = field;
            FilteredItems[idx].CurrentMatchPos = pos;
            _prevCurrentIndex = idx;
            _ = BringMatchRowIntoViewAsync(idx);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_searchPending)
            {
                _searchTimer.Stop();
                ApplySearch();
                return;
            }
            if (_matches.Count == 0) return;
            _currentMatchIndex = (_currentMatchIndex + 1) % _matches.Count;
            HighlightCurrentMatch();
            MatchCountText.Text = $"{_currentMatchIndex + 1}/{_matches.Count}";
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_searchPending)
            {
                _searchTimer.Stop();
                ApplySearch();
                return;
            }
            if (_matches.Count == 0) return;
            _currentMatchIndex = (_currentMatchIndex - 1 + _matches.Count) % _matches.Count;
            HighlightCurrentMatch();
            MatchCountText.Text = $"{_currentMatchIndex + 1}/{_matches.Count}";
        }

        private async Task BringMatchRowIntoViewAsync(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= FilteredItems.Count)
                return;

            int version = ++_bringIntoViewVersion;
            await Task.Delay(40);
            if (version != _bringIntoViewVersion)
                return;

            var scrollViewer = FindVisualDescendants<ScrollViewer>(FeaturesTableView)
                .OrderByDescending(viewer => viewer.ScrollableHeight)
                .FirstOrDefault();
            if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0)
                return;

            var visibleRows = GetVisibleMatchRows();
            double averageRowHeight = visibleRows.Count > 0
                ? visibleRows.Average(row => row.Row.ActualHeight)
                : 40;

            if (!visibleRows.Any(row => row.Index == targetIndex))
            {
                double initialOffset = Math.Max(0, targetIndex * averageRowHeight - averageRowHeight);
                scrollViewer.ChangeView(null, Math.Min(initialOffset, scrollViewer.ScrollableHeight), null, true);
            }

            for (int attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(50);
                if (version != _bringIntoViewVersion)
                    return;

                visibleRows = GetVisibleMatchRows();
                var matchRow = visibleRows.FirstOrDefault(row => row.Index == targetIndex).Row;
                if (matchRow != null)
                {
                    matchRow.StartBringIntoView();
                    return;
                }

                if (visibleRows.Count == 0)
                    continue;

                averageRowHeight = visibleRows.Average(row => Math.Max(1, row.Row.ActualHeight));
                int visibleMiddleIndex = visibleRows[visibleRows.Count / 2].Index;
                double correctedOffset = scrollViewer.VerticalOffset + (targetIndex - visibleMiddleIndex) * averageRowHeight;
                correctedOffset = Math.Max(0, Math.Min(correctedOffset, scrollViewer.ScrollableHeight));
                scrollViewer.ChangeView(null, correctedOffset, null, true);
            }
        }

        private List<(int Index, TableViewRow Row)> GetVisibleMatchRows()
        {
            var rows = new List<(int Index, TableViewRow Row)>();
            foreach (var row in FindVisualDescendants<TableViewRow>(FeaturesTableView))
            {
                if (row.DataContext is FeatureEntry item)
                {
                    int index = FilteredItems.IndexOf(item);
                    if (index >= 0 && row.ActualHeight > 0)
                        rows.Add((index, row));
                }
            }
            return rows.OrderBy(row => row.Index).ToList();
        }

        private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T match)
                yield return match;

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                foreach (var child in FindVisualDescendants<T>(VisualTreeHelper.GetChild(root, i)))
                    yield return child;
            }
        }

        private static readonly System.Text.RegularExpressions.Regex _idPattern =
    new(@"^\s*\d{8,}(\s*,\s*\d{8,})*\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string GetSelectedTextFromRow(DependencyObject root)
        {
            if (root is TextBlock rootTb && !string.IsNullOrEmpty(rootTb.SelectedText))
                return rootTb.SelectedText;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is TextBlock tb && !string.IsNullOrEmpty(tb.SelectedText))
                    return tb.SelectedText;
                var inner = GetSelectedTextFromRow(c);
                if (!string.IsNullOrEmpty(inner)) return inner;
            }
            return null;
        }

        private static TextBlock FindFocusedTextBlock(DependencyObject root)
        {
            if (root is TextBlock rootTb && !string.IsNullOrEmpty(rootTb.SelectedText))
                return rootTb;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is TextBlock tb && !string.IsNullOrEmpty(tb.SelectedText))
                    return tb;
                var inner = FindFocusedTextBlock(c);
                if (inner != null) return inner;
            }
            return null;
        }

        private static string NormalizeIdCsv(string raw)
        {
            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
                parts[i] = parts[i].Trim();
            return string.Join(",", parts);
        }

        private void FeatureRowMenuFlyout_Opening(object sender, object e)
        {
            if (sender is not MenuFlyout menu)
                return;

            var target = menu.Target as DependencyObject;
            var root = target;
            if (root == null)
                return;

            var fe = FindFeatureEntry(target);
            if (fe == null)
                return;

            PrepareRowContext(target ?? root, fe, out var isCopyId);

            var rl = new ResourceLoader();
            var menuItems = menu.Items.OfType<MenuFlyoutItem>().ToList();
            if (menuItems.Count >= 5)
            {
                menuItems[3].Text = isCopyId
                    ? rl.GetString("Menu_CopyFeatureId/Text")
                    : rl.GetString("Menu_CopyText/Text");
                menuItems[4].Visibility = _ctxCopySourceTb != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static FeatureEntry? FindFeatureEntry(DependencyObject? target)
        {
            while (target != null)
            {
                if (target is FrameworkElement element)
                {
                    if (element.DataContext is FeatureEntry item)
                        return item;
                    if (element.Tag is FeatureEntry taggedItem)
                        return taggedItem;
                }

                target = VisualTreeHelper.GetParent(target);
            }

            return null;
        }

        private void PrepareRowContext(DependencyObject root, FeatureEntry fe, out bool isCopyId)
        {
            var selectedTextBlock = FindFocusedTextBlock(root);
            string selectedText = selectedTextBlock?.SelectedText ?? GetSelectedTextFromRow(root);
            bool hasSelection = !string.IsNullOrEmpty(selectedText);
            bool isIdSelection = hasSelection && _idPattern.IsMatch(selectedText);
            _ctxIdCsv = isIdSelection ? NormalizeIdCsv(selectedText) : fe.Id;
            _ctxVariantOptions = !hasSelection && _ctxIdCsv == fe.Id ? fe.Variant : "";
            _ctxCopyContent = hasSelection ? selectedText : fe.Id;
            isCopyId = !hasSelection;
            _ctxCopySourceTb = selectedTextBlock;
        }
        private void ShowRowCommandBarFlyout(FrameworkElement target, Point pos, string idCsv, string copyContent, bool isCopyId, TextBlock copySourceTb)
        {
            _ctxIdCsv = idCsv;
            _ctxCopyContent = copyContent;
            _ctxCopySourceTb = copySourceTb;
            var rl = new ResourceLoader();
            CmdCopy.Text = isCopyId
                ? rl.GetString("Menu_CopyFeatureId/Text")
                : rl.GetString("Menu_CopyText/Text");
            CmdSelectAll.Visibility = copySourceTb != null ? Visibility.Visible : Visibility.Collapsed;
            RowContextMenu.ShowAt(target, new FlyoutShowOptions { Position = pos });
        }

        private async void CmdEnable_Click(object sender, RoutedEventArgs e) => await ExecuteFeatureAsync(_ctxIdCsv, "Enable", _ctxVariantOptions);

        private async void CmdDisable_Click(object sender, RoutedEventArgs e) => await ExecuteFeatureAsync(_ctxIdCsv, "Disable");

        private async void CmdRestore_Click(object sender, RoutedEventArgs e) => await ExecuteFeatureAsync(_ctxIdCsv, "Restore");

        private void CmdCopy_Click(object sender, RoutedEventArgs e)
        {
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(_ctxCopyContent);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        }

        private void CmdSelectAll_Click(object sender, RoutedEventArgs e) => _ctxCopySourceTb?.SelectAll();

        private async System.Threading.Tasks.Task ExecuteFeatureAsync(string idCsv, string action, string variantOptions = null)
        {
            var rl = new ResourceLoader();
            string busyText = action switch
            {
                "Enable" => rl.GetString("EnableDisable_EnableButton/Text"),
                "Disable" => rl.GetString("EnableDisable_DisableButton/Text"),
                "Restore" => rl.GetString("EnableDisable_RestoreButton/Text"),
                _ => ""
            };

            string variant = null;
            string payloadKind = null;
            string payload = null;
            if (action == "Enable" && !string.IsNullOrWhiteSpace(variantOptions))
            {
                var firstVariant = System.Text.RegularExpressions.Regex.Match(variantOptions, @"\d+").Value;
                if (!string.IsNullOrWhiteSpace(firstVariant))
                {
                    var variantDialog = new ViVeToolGUI.Dialogs.VariantSelectionDialog(firstVariant)
                    {
                        XamlRoot = this.Content.XamlRoot
                    };
                    if (await variantDialog.ShowAsync() != ContentDialogResult.Primary)
                        return;

                    variant = variantDialog.VariantNumberText;
                    payloadKind = variantDialog.PayloadKind;
                    payload = variantDialog.PayloadText;
                }
            }

            ShowLoadingOverlay(busyText);
            MainWindow.Instance?.ShowTaskbarIndeterminate();

            var page = ViVeToolGUI.Pages.EnableDisablePage.Instance;
            if (page == null)
            {
                HideLoadingOverlay();
                MainWindow.Instance?.ShowTaskbarError();
                await ShowErrorAsync("EnableDisablePage not initialized.");
                return;
            }

            var (ok, msg) = await page.RunFeatureCommandAsync(idCsv, action, variant, payloadKind, payload);
            HideLoadingOverlay();

            if (ok)
            {
                MainWindow.Instance?.ShowTaskbarCompleted();
                var dlg = new ViVeToolGUI.Dialogs.SuccessDialog(msg) { XamlRoot = this.Content.XamlRoot };
                await dlg.ShowAsync();
            }
            else
            {
                MainWindow.Instance?.ShowTaskbarError();
                await ShowErrorAsync(msg);
            }
        }

        private async System.Threading.Tasks.Task ShowErrorAsync(string message)
        {
            var dlg = new ViVeToolGUI.Dialogs.ErrorDialog(message) { XamlRoot = this.Content.XamlRoot };
            await dlg.ShowAsync();
        }

        private void ShowLoadingOverlay(string text)
        {
            if (LoadingText != null) LoadingText.Text = text ?? "";
            if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Visible;
        }

        private void HideLoadingOverlay()
        {
            if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        private void LoadCsv()
        {
            FilteredItems.Clear();
            _allItems.Clear();
            string lang = GetAppLanguage();
            string localPath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Strings", lang, "Features.csv");
            string csvPath = File.Exists(localPath)
                ? localPath
                : Path.Combine(AppContext.BaseDirectory, "Strings", lang, "Features.csv");
            if (!File.Exists(csvPath)) return;
            foreach (var line in File.ReadLines(csvPath).Skip(1))
            {
                var parts = ParseCsvLine(line);
                if (parts.Count >= 3)
                {
                    var entry = new FeatureEntry { Id = parts[0], Variant = parts[1], Description = parts[2] };
                    _allItems.Add(entry);
                    FilteredItems.Add(entry);
                }
            }
        }

        private static string GetAppLanguage()
        {
            var primary = ApplicationLanguages.PrimaryLanguageOverride;
            if (!string.IsNullOrEmpty(primary))
                return primary.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
            var langs = GlobalizationPreferences.Languages;
            if (langs.Count > 0 && langs[0].StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return "zh-CN";
            return "en-US";
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    { current.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            result.Add(current.ToString());
            return result;
        }

        private async Task FetchRemoteCsvAsync()
        {
            _fetchCts?.Cancel();
            _fetchCts = new CancellationTokenSource();
            var ct = _fetchCts.Token;
            string[] langs = { "zh-CN", "en-US" };
            string currentLang = GetAppLanguage();
            ShowInfoBar(InfoBarSeverity.Informational, new ResourceLoader().GetString("InfoBar_Fetching"));
            try
            {
                var fetchedData = new Dictionary<string, string>();
                foreach (var lang in langs)
                {
                    string url = $"https://furry-xiyi.github.io/ViVe-Feature-ID/{lang}/Features.csv";
                    var response = await _httpClient.GetAsync(url, ct);
                    response.EnsureSuccessStatusCode();
                    fetchedData[lang] = await response.Content.ReadAsStringAsync();
                    ct.ThrowIfCancellationRequested();
                }
                var localBase = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                foreach (var kv in fetchedData)
                {
                    string dir = Path.Combine(localBase, "Strings", kv.Key);
                    Directory.CreateDirectory(dir);
                    await File.WriteAllTextAsync(Path.Combine(dir, "Features.csv"), kv.Value, ct);
                }
                if (fetchedData.TryGetValue(currentLang, out var csv))
                {
                    var newItems = new List<FeatureEntry>();
                    foreach (var line in csv.Split('\n').Skip(1))
                    {
                        var trimmed = line.TrimEnd('\r');
                        if (string.IsNullOrWhiteSpace(trimmed)) continue;
                        var parts = ParseCsvLine(trimmed);
                        if (parts.Count >= 3)
                            newItems.Add(new FeatureEntry { Id = parts[0], Variant = parts[1], Description = parts[2] });
                    }
                    if (newItems.Count > 0)
                    {
                        _allItems = newItems;
                        FilteredItems.Clear();
                        foreach (var item in _allItems)
                            FilteredItems.Add(item);
                        _matches.Clear();
                        _currentMatchIndex = -1;
                        MatchCountText.Text = "";
                        if (!string.IsNullOrEmpty(SearchBox.Text))
                            ApplySearch();
                    }
                }
                StopSpinning();
                RefreshButton.Visibility = Visibility.Collapsed;
                UpdateDragRects();
                ShowInfoBar(InfoBarSeverity.Success, new ResourceLoader().GetString("InfoBar_FetchSuccess"));
                ScheduleDismissInfoBar(3000);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken != ct)
            {
                StopSpinning();
                RefreshButton.Visibility = Visibility.Visible;
                UpdateDragRects();
                ShowInfoBar(InfoBarSeverity.Warning, new ResourceLoader().GetString("InfoBar_FetchTimeout"));
                ScheduleDismissInfoBar(4000);
            }
            catch (OperationCanceledException)
            {
                StopSpinning();
            }
            catch (Exception ex)
            {
                StopSpinning();
                RefreshButton.Visibility = Visibility.Visible;
                UpdateDragRects();
                var loader = new ResourceLoader();
                ShowInfoBar(InfoBarSeverity.Error, string.Format(loader.GetString("InfoBar_FetchError"), ex.Message));
                ScheduleDismissInfoBar(5000);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            StartSpinning();
            _ = FetchRemoteCsvAsync();
        }

        private void ShowInfoBar(InfoBarSeverity severity, string message)
        {
            _infoBarDismissTimer?.Stop();
            StatusInfoBar.Severity = severity;
            StatusInfoBar.Message = message;
            StatusInfoBar.IsOpen = true;
            InfoBarSlideIn.Begin();
        }

        private void HideInfoBar()
        {
            InfoBarSlideOut.Begin();
            InfoBarSlideOut.Completed -= InfoBarSlideOut_Completed;
            InfoBarSlideOut.Completed += InfoBarSlideOut_Completed;
        }

        private void InfoBarSlideOut_Completed(object? sender, object e)
        {
            StatusInfoBar.IsOpen = false;
            InfoBarSlideOut.Completed -= InfoBarSlideOut_Completed;
        }

        private void ScheduleDismissInfoBar(int ms)
        {
            _infoBarDismissTimer?.Stop();
            _infoBarDismissTimer!.Interval = TimeSpan.FromMilliseconds(ms);
            _infoBarDismissTimer.Start();
        }

        private void StartSpinning()
        {
            if (_isSpinning) return;
            _isSpinning = true;

            _spinStoryboard = new Storyboard();
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(1)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(anim, RefreshIconRotation);
            Storyboard.SetTargetProperty(anim, "Angle");
            _spinStoryboard.Children.Add(anim);
            _spinStoryboard.Begin();
        }

        private void StopSpinning()
        {
            if (!_isSpinning) return;
            _isSpinning = false;
            _spinStoryboard?.Stop();
            _spinStoryboard = null;
            RefreshIconRotation.Angle = 0;
        }
    }
}
