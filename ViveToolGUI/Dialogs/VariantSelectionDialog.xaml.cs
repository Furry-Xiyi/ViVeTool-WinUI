using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ViVeToolGUI.Controls;
using Windows.ApplicationModel.Resources;

namespace ViVeToolGUI.Dialogs
{
    public sealed partial class VariantSelectionDialog : ContentDialog
    {
        public string VariantNumberText => Options.VariantNumberText;
        public string PayloadKind => Options.PayloadKind;
        public string PayloadText => Options.PayloadText;

        public VariantSelectionDialog(string variant, string payloadKind = null, string payload = null)
        {
            InitializeComponent();
            var loader = ResourceLoader.GetForViewIndependentUse();
            Title = GetResourceText(loader, "EnableDisable_Variant");
            PrimaryButtonText = GetResourceText(loader, "Dialog_Confirm");
            CloseButtonText = GetResourceText(loader, "Dialog_Cancel");
            Options.SetCustomVariant(variant, payloadKind, payload);
        }

        private void VariantSelectionDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (Options.VariantMode == "Custom" && string.IsNullOrWhiteSpace(Options.VariantNumberText))
            {
                ValidationText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            ValidationText.Visibility = Visibility.Collapsed;
        }

        private static string GetResourceText(ResourceLoader loader, string key)
        {
            string value = loader.GetString(key);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            value = loader.GetString($"{key}/Text");
            return string.IsNullOrWhiteSpace(value) ? key : value;
        }
    }
}
