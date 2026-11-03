using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Resources;
using Windows.Storage;
using Windows.System;
using Windows.System.UserProfile;

namespace ViVeToolGUI.Pages
{
    public sealed class FeatureInfo : System.ComponentModel.INotifyPropertyChanged
    {
        private string _state = "";
        public string Id { get; set; } = "";

        public string State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged(nameof(State));
                }
            }
        }
        public string Type { get; set; } = "";
        public string Priority { get; set; } = "";
        public string Variant { get; set; } = "";
        public string VariantPayloadKind { get; set; } = "";
        public string VariantPayloadValue { get; set; } = "";
        public string Description { get; set; } = "";

        public bool HasVariant => !string.IsNullOrWhiteSpace(Variant) && Variant != "0";

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    public sealed partial class QueryPage : Page
    {
        private readonly ResourceLoader _resourceLoader;
        private readonly ObservableCollection<FeatureInfo> _features = new();
        private FeatureInfo[] _allFeatures = Array.Empty<FeatureInfo>();
        private readonly Dictionary<string, string> _featureDescriptions = new(StringComparer.OrdinalIgnoreCase);

        public QueryPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
            _resourceLoader = ResourceLoader.GetForViewIndependentUse();
            LoadFeatureDescriptions();
            FeaturesListView.ItemsSource = _features;
        }

        private async void QueryAllButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            QueryAllButton.IsEnabled = false;
            ExportButton.IsEnabled = false;
            SearchBox.IsEnabled = false;
            _features.Clear();
            LoadFeatureDescriptions();

            MainWindow.Instance?.ShowTaskbarIndeterminate();

            try
            {
                MainWindow.Instance?.ShowLoadingOverlay(_resourceLoader.GetString("Query_Loading"));

                if (!await App.EnsureViVeToolInitializedAsync())
                    throw new InvalidOperationException(_resourceLoader.GetString("ViVeTool_Initializing"));

                var result = await MainWindow.ExecuteViVeToolCommandAsync("/query");

                if (result.ExitCode != 0)
                    throw new InvalidOperationException(GetCommandError(result));

                var tempList = new System.Collections.Generic.List<FeatureInfo>();
                int count = 0;

                foreach (var feature in StreamParseQueryOutput(result.Output))
                {
                    _features.Add(feature);
                    tempList.Add(feature);

                    if (++count % 10 == 0)
                    {
                        await Task.Yield();
                    }
                }

                _allFeatures = tempList.ToArray();
                ExportButton.IsEnabled = _allFeatures.Length > 0;

                MainWindow.Instance?.ShowTaskbarCompleted();

                DispatcherQueue.TryEnqueue(() =>
                {
                    bool enableNotification = Windows.Storage.ApplicationData.Current.LocalSettings.Values["EnableQueryNotification"] as bool? ?? true;

                    if (enableNotification && MainWindow.Instance != null && !MainWindow.Instance.IsWindowActivated)
                    {
                        string title = _resourceLoader.GetString("Query_NotificationTitle");
                        string body = string.Format(
                            _resourceLoader.GetString("Query_NotificationBody"),
                            _allFeatures.Length);

                        MainWindow.Instance.ShowNotification(title, body);
                        MainWindow.Instance.SetBadge(1);
                    }
                });
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowTaskbarError();

                var dialog = new Dialogs.ErrorDialog(ex.Message) { XamlRoot = this.XamlRoot };
                await dialog.ShowAsync();
            }
            finally
            {
                QueryAllButton.IsEnabled = true;
                SearchBox.IsEnabled = true;
                MainWindow.Instance?.HideLoadingOverlay();
            }
        }

        private System.Collections.Generic.IEnumerable<FeatureInfo> StreamParseQueryOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                yield break;

            output = Regex.Replace(output, @"\x1B\[[0-9;]*m", "");
            output = output.Replace("\uFEFF", "");

            var lines = Regex.Split(output, @"\r\n|\n|\r")
                .Select(x => x.TrimEnd())
                .Where(x => !string.IsNullOrWhiteSpace(x));

            FeatureInfo current = null;
            var processedIds = new System.Collections.Generic.HashSet<string>();

            foreach (string line in lines)
            {
                var idMatch = Regex.Match(line, @"\[(\d+)\](?:\s*\(([^)]+)\))?");
                if (idMatch.Success)
                {
                    if (current != null && !processedIds.Contains(current.Id))
                    {
                        processedIds.Add(current.Id);
                        yield return current;
                    }

                    current = new FeatureInfo
                    {
                        Id = Clean(idMatch.Groups[1].Value),
                        Description = GetFeatureDescription(idMatch.Groups[1].Value,
                            idMatch.Groups[2].Success ? Clean(idMatch.Groups[2].Value.Trim()) : "")
                    };
                    continue;
                }

                if (current == null) continue;

                if (line.Contains("State", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"State\s*:\s*([A-Za-z]+)", RegexOptions.IgnoreCase);
                    if (match.Success) current.State = Clean(match.Groups[1].Value);
                }
                else if (line.Contains("Priority", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"Priority\s*:\s*([A-Za-z]+|\d+)", RegexOptions.IgnoreCase);
                    if (match.Success) current.Priority = Clean(match.Groups[1].Value);
                }
                else if (line.Contains("Variant", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"Variant\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (match.Success) current.Variant = Clean(match.Groups[1].Value);
                }
                else if (line.Contains("Type", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"Type\s*:\s*([A-Za-z]+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                        current.Type = Clean(match.Groups[1].Value);
                }
                else if (line.Contains("PayloadKind", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"PayloadKind\s*:\s*([A-Za-z]+)", RegexOptions.IgnoreCase);
                    if (match.Success) current.VariantPayloadKind = Clean(match.Groups[1].Value);
                }
                else if (line.Contains("Payload", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"Payload\s*:\s*(\S+)", RegexOptions.IgnoreCase);
                    if (match.Success) current.VariantPayloadValue = Clean(match.Groups[1].Value);
                }
            }

            if (current != null && !processedIds.Contains(current.Id))
            {
                yield return current;
            }
        }

        private void LoadFeatureDescriptions()
        {
            _featureDescriptions.Clear();
            string languageFolder = GetFeatureTextFolder();
            string localPath = Path.Combine(ApplicationData.Current.LocalFolder.Path,
                "Strings", languageFolder, "Features.csv");
            string csvPath = File.Exists(localPath)
                ? localPath
                : Path.Combine(AppContext.BaseDirectory, "Strings", languageFolder, "Features.csv");

            if (!File.Exists(csvPath))
                return;

            try
            {
                foreach (string line in File.ReadLines(csvPath).Skip(1))
                {
                    var fields = ParseCsvLine(line);
                    if (fields.Count < 3 || string.IsNullOrWhiteSpace(fields[0]) || string.IsNullOrWhiteSpace(fields[2]))
                        continue;

                    _featureDescriptions[fields[0].Trim()] = Clean(fields[2].Trim());
                }
            }
            catch (IOException)
            {
                // Fall back to the optional display name from ViVeTool's /query output.
            }
            catch (UnauthorizedAccessException)
            {
                // Fall back to the optional display name from ViVeTool's /query output.
            }
        }

        private string GetFeatureDescription(string id, string fallbackName)
        {
            return _featureDescriptions.TryGetValue(id, out string description)
                ? description
                : fallbackName ?? "";
        }

        private static System.Collections.Generic.List<string> ParseCsvLine(string line)
        {
            var fields = new System.Collections.Generic.List<string>();
            var value = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char current = line[i];
                if (current == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (current == ',' && !quoted)
                {
                    fields.Add(value.ToString());
                    value.Clear();
                }
                else
                {
                    value.Append(current);
                }
            }

            fields.Add(value.ToString());
            return fields;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string text = SearchBox.Text?.Trim() ?? "";

            var filtered = string.IsNullOrWhiteSpace(text)
                ? _allFeatures
                : _allFeatures.Where(x =>
                    x.Id.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.State.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.Type.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.Priority.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.Variant.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.Description.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.VariantPayloadKind.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.VariantPayloadValue.Contains(text, StringComparison.OrdinalIgnoreCase));

            _features.Clear();
            foreach (var item in filtered)
                _features.Add(item);
        }

        private async void EnableFeature_Click(object sender, RoutedEventArgs e)
        {
            await ApplyFromMenuAsync(sender, true);
        }

        private async void DisableFeature_Click(object sender, RoutedEventArgs e)
        {
            await ApplyFromMenuAsync(sender, false);
        }

        private void MenuFlyout_Opening(object sender, object e)
        {
            if (sender is MenuFlyout menu)
            {
                if (menu.Target is FrameworkElement targetElement && targetElement.DataContext is FeatureInfo feature)
                {
                    foreach (var menuItem in menu.Items.OfType<MenuFlyoutItem>())
                        menuItem.CommandParameter = feature;

                    var enableItem = menu.Items.FirstOrDefault(i => i is MenuFlyoutItem m && m.Name == "MenuEnable");
                    var disableItem = menu.Items.FirstOrDefault(i => i is MenuFlyoutItem m && m.Name == "MenuDisable");

                    if (enableItem != null)
                    {
                        enableItem.Visibility = feature.State.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
                            ? Visibility.Collapsed : Visibility.Visible;
                    }

                    if (disableItem != null)
                    {
                        disableItem.Visibility = feature.State.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                            ? Visibility.Collapsed : Visibility.Visible;
                    }
                }
            }
        }

        private async void RestoreFeature_Click(object sender, RoutedEventArgs e)
        {
            if (GetFeature(sender) is not FeatureInfo feature || !uint.TryParse(feature.Id, out uint id))
                return;

            MainWindow.Instance?.ShowLoadingOverlay(_resourceLoader.GetString("Query_Loading"));
            MainWindow.Instance?.ShowTaskbarIndeterminate();

            try
            {
                var result = await MainWindow.ExecuteViVeToolCommandAsync($"/reset /id:{id}");

                if (result.ExitCode != 0)
                    throw new InvalidOperationException(GetCommandError(result));

                feature.State = "Default";
                OperationLogger.Append(feature.Id, "Restore");

                MainWindow.Instance?.ShowTaskbarCompleted();
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowTaskbarError();
                await new Dialogs.ErrorDialog(ex.Message) { XamlRoot = this.XamlRoot }.ShowAsync();
            }
            finally
            {
                MainWindow.Instance?.HideLoadingOverlay();
            }
        }

        private async Task ApplyFromMenuAsync(object sender, bool enable)
        {
            if (GetFeature(sender) is not FeatureInfo feature || !uint.TryParse(feature.Id, out uint id))
                return;

            string variant = null;
            string payloadKind = null;
            string payload = null;
            if (enable && feature.HasVariant)
            {
                var variantDialog = new Dialogs.VariantSelectionDialog(
                    feature.Variant,
                    feature.VariantPayloadKind,
                    feature.VariantPayloadValue)
                {
                    XamlRoot = this.XamlRoot
                };
                if (await variantDialog.ShowAsync() != ContentDialogResult.Primary)
                    return;

                variant = variantDialog.VariantNumberText;
                payloadKind = variantDialog.PayloadKind;
                payload = variantDialog.PayloadText;
            }

            MainWindow.Instance?.ShowLoadingOverlay(_resourceLoader.GetString("Query_Loading"));
            MainWindow.Instance?.ShowTaskbarIndeterminate();

            try
            {
                string command = enable ? "/enable" : "/disable";
                string arguments = $"{command} /id:{id}";
                if (enable && !string.IsNullOrWhiteSpace(variant))
                {
                    arguments += $" /variant:{variant}";
                    if (!string.IsNullOrWhiteSpace(payloadKind) && !payloadKind.Equals("None", StringComparison.OrdinalIgnoreCase))
                    {
                        arguments += $" /variantpayloadkind:{payloadKind}";
                        if (!string.IsNullOrWhiteSpace(payload))
                            arguments += $" /variantpayload:{payload}";
                    }
                }

                var result = await MainWindow.ExecuteViVeToolCommandAsync(arguments);

                if (result.ExitCode != 0)
                    throw new InvalidOperationException(GetCommandError(result));

                feature.State = enable ? "Enabled" : "Disabled";
                OperationLogger.Append(feature.Id, enable ? "Enable" : "Disable", variant, payloadKind, payload);

                MainWindow.Instance?.ShowTaskbarCompleted();

                var dialog = new Dialogs.SuccessDialog(string.IsNullOrWhiteSpace(result.Output)
                    ? _resourceLoader.GetString("Command_Success")
                    : result.Output);
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowTaskbarError();
                await new Dialogs.ErrorDialog(ex.Message) { XamlRoot = this.XamlRoot }.ShowAsync();
            }
            finally
            {
                MainWindow.Instance?.HideLoadingOverlay();
            }
        }

        private void CopyFeatureId_Click(object sender, RoutedEventArgs e)
        {
            if (GetFeature(sender) is not FeatureInfo feature)
                return;

            var dataPackage = new DataPackage();
            dataPackage.SetText(feature.Id);
            Clipboard.SetContent(dataPackage);
        }

        private FeatureInfo GetFeature(object sender)
        {
            if (sender is MenuFlyoutItem item && item.CommandParameter is FeatureInfo feature)
                return feature;

            return null;
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_allFeatures.Length == 0)
                    return;

                // 1. 先展示导出选项对话框
                var optionsDialog = new Dialogs.ExportOptionsDialog();
                optionsDialog.XamlRoot = this.XamlRoot;

                var optionsResult = await optionsDialog.ShowAsync();

                // 如果未选择Primary（确认导出），则取消
                if (optionsResult != ContentDialogResult.Primary)
                    return;

                ExportButton.IsEnabled = false;

                var documentsFolder = await StorageFolder.GetFolderFromPathAsync(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

                bool isCsv = optionsDialog.ExportCsv;
                string extension = isCsv ? "csv" : "txt";
                string fileName = $"{_resourceLoader.GetString("Export_FilePrefix")}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";

                var file = await documentsFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                var sb = new StringBuilder();

                string idHeader = GetResourceText("Query_HeaderID");
                string stateHeader = GetResourceText("Query_HeaderState");
                string typeHeader = GetResourceText("Query_HeaderType");
                string variantHeader = GetResourceText("Query_HeaderVariant");
                string descriptionHeader = GetResourceText("Query_HeaderVariantPayload");

                // 根据用户选项输出内容
                if (isCsv)
                {
                    sb.AppendLine($"{idHeader},{stateHeader},{typeHeader},{variantHeader},{descriptionHeader}");
                    foreach (var f in _allFeatures)
                    {
                        sb.AppendLine($"{f.Id},{f.State},{f.Type},{f.Variant},{f.Description}");
                    }
                }
                else
                {
                    foreach (var f in _allFeatures)
                    {
                        sb.AppendLine($"ID: {f.Id} | State: {f.State} | Type: {f.Type} | Variant: {f.Variant} | Description: {f.Description}");
                    }
                }

                await FileIO.WriteTextAsync(file, sb.ToString());

                // 2. 导出完成后显示 SuccessDialog
                var openDialog = new Dialogs.SuccessDialog(
    _resourceLoader.GetString("Export_Success"),
    file.Path);
                openDialog.XamlRoot = this.XamlRoot;
                await openDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new Dialogs.ErrorDialog(ex.Message);
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
            }
            finally
            {
                ExportButton.IsEnabled = _allFeatures.Length > 0;
            }
        }

        private void ViewButton_Click(object sender, RoutedEventArgs e)
        {
            AppWindows.TextFileViewerWindow.ShowOrActivate();
        }

        private string GetFeatureTextFolder()
        {
            string language = "";

            try
            {
                language = GlobalizationPreferences.Languages.FirstOrDefault() ?? "";
            }
            catch { }

            if (string.IsNullOrWhiteSpace(language))
                language = CultureInfo.CurrentUICulture.Name;

            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return "zh-CN";

            return "en-US";
        }

        private string GetResourceText(string key)
        {
            string value = _resourceLoader.GetString(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            value = _resourceLoader.GetString($"{key}/Text");
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return key;
        }

        private string EscapeMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            return text
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var cleaned = new string(text.Where(c => !char.IsControl(c)).ToArray());
            return cleaned.Length > 10000 ? cleaned.Substring(0, 10000) : cleaned;
        }

        private string GetCommandError(CommandResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.Output))
                return result.Output;

            if (!string.IsNullOrWhiteSpace(result.Error))
                return result.Error;

            return $"ViVeTool exited with code {result.ExitCode}.";
        }
    }
}
