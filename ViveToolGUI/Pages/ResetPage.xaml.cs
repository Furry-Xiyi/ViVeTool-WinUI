using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;

namespace ViVeToolGUI.Pages
{
    public sealed partial class ResetPage : Page
    {
        private readonly ResourceLoader _resourceLoader;

        public ResetPage()
        {
            this.InitializeComponent();
            _resourceLoader = ResourceLoader.GetForViewIndependentUse();
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteResetAsync(false);
        }

        private async void ResetManualButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteResetAsync(true);
        }

        private async Task ExecuteResetAsync(bool manualOnly)
        {
            ResetButton.IsEnabled = false;
            ResetManualButton.IsEnabled = false;

            MainWindow.Instance?.ShowLoadingOverlay(_resourceLoader.GetString("Reset_Breadcrumb"));
            MainWindow.Instance?.ShowTaskbarIndeterminate();

            try
            {
                if (manualOnly)
                {
                    var featureIds = GetManualFeatureIds();
                    if (featureIds.Count == 0)
                    {
                        MainWindow.Instance?.ClearTaskbarProgress();
                        await ShowErrorAsync(_resourceLoader.GetString("Reset_ManualEmptyMessage"));
                        return;
                    }

                    var confirmDialog = new Dialogs.ConfirmDialog(
                        _resourceLoader.GetString("Reset_ConfirmTitle"),
                        _resourceLoader.GetString("Reset_ManualConfirmMessage"))
                    {
                        XamlRoot = this.XamlRoot
                    };

                    if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                    {
                        MainWindow.Instance?.ClearTaskbarProgress();
                        return;
                    }

                    await HandleResetResultAsync(await ResetFeatureIdsAsync(featureIds));
                    return;
                }

                bool confirmed = false;
                bool confirmationRequested = false;
                var result = await MainWindow.ExecuteViVeToolCommandAsync("/fullreset", async () =>
                {
                    confirmationRequested = true;
                    var confirmDialog = new Dialogs.ConfirmDialog(
                        _resourceLoader.GetString("Reset_ConfirmTitle"),
                        _resourceLoader.GetString("Reset_ConfirmMessage"))
                    {
                        XamlRoot = this.XamlRoot
                    };

                    confirmed = await confirmDialog.ShowAsync() == ContentDialogResult.Primary;
                    return confirmed;
                });

                if (confirmationRequested && !confirmed)
                {
                    MainWindow.Instance?.ClearTaskbarProgress();
                    return;
                }

                await HandleResetResultAsync(result);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowTaskbarError();
                await ShowErrorAsync(ex.Message);
            }
            finally
            {
                MainWindow.Instance?.HideLoadingOverlay();
                ResetButton.IsEnabled = true;
                ResetManualButton.IsEnabled = true;
            }
        }

        private HashSet<string> GetManualFeatureIds()
        {
            var featureIds = new HashSet<string>(StringComparer.Ordinal);
            var processedIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var record in OperationLogger.Load())
            {
                foreach (string value in record.FeatureId.Split(
                    new[] { ',', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (uint.TryParse(value, out uint id))
                    {
                        string featureId = id.ToString();
                        if (processedIds.Add(featureId) &&
                            (record.Operation.Equals("Enable", StringComparison.OrdinalIgnoreCase) ||
                             record.Operation.Equals("Disable", StringComparison.OrdinalIgnoreCase)))
                        {
                            featureIds.Add(featureId);
                        }
                    }
                }
            }

            return featureIds;
        }

        private async Task<CommandResult> ResetFeatureIdsAsync(HashSet<string> featureIds)
        {
            var output = new List<string>();
            var ids = featureIds.ToArray();

            const int idsPerCommand = 100;
            for (int i = 0; i < ids.Length; i += idsPerCommand)
            {
                string idList = string.Join(",", ids.Skip(i).Take(idsPerCommand));
                var result = await MainWindow.ExecuteViVeToolCommandAsync($"/reset /id:{idList} /store:both");
                if (result.ExitCode != 0)
                    return result;

                OperationLogger.Append(idList, "Restore");
                if (!string.IsNullOrWhiteSpace(result.Output))
                    output.Add(result.Output);
            }

            return new CommandResult { ExitCode = 0, Output = string.Join(Environment.NewLine, output) };
        }

        private async Task HandleResetResultAsync(CommandResult result)
        {
            if (result.ExitCode == 0)
            {
                MainWindow.Instance?.ShowTaskbarCompleted();
                var dialog = new Dialogs.SuccessDialog(_resourceLoader.GetString("Reset_SuccessMessage"))
                {
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            MainWindow.Instance?.ShowTaskbarError();
            await ShowErrorAsync(GetCommandError(result));
        }

        private async Task ShowErrorAsync(string message)
        {
            var dialog = new Dialogs.ErrorDialog(message)
            {
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
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
