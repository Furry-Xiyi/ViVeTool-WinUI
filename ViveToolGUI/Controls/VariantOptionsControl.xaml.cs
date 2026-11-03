using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;

namespace ViVeToolGUI.Controls
{
    public sealed partial class VariantOptionsControl : UserControl
    {
        public string VariantMode => VariantModeComboBox.SelectedIndex == 1 ? "Custom" : "None";

        public string VariantNumberText => FormatUInt32(VariantNumberBox.Value);

        public string PayloadKind => VariantPayloadKindComboBox.SelectedIndex switch
        {
            1 => "Resident",
            2 => "External",
            _ => "None"
        };

        public string PayloadText => FormatUInt32(VariantPayloadNumberBox.Value);

        public VariantOptionsControl()
        {
            InitializeComponent();
        }

        public void SetCustomVariant(string variant, string payloadKind = null, string payload = null)
        {
            VariantModeComboBox.SelectedIndex = 1;
            if (TryParseUInt32(variant, out uint variantNumber))
                VariantNumberBox.Value = variantNumber;

            VariantPayloadKindComboBox.SelectedIndex = payloadKind?.ToLowerInvariant() switch
            {
                "resident" => 1,
                "external" => 2,
                _ => 0
            };

            if (VariantPayloadKindComboBox.SelectedIndex != 0 && TryParseUInt32(payload, out uint payloadNumber))
                VariantPayloadNumberBox.Value = payloadNumber;

            VariantExpander.IsExpanded = true;
            UpdateVisibility();
        }

        public void SetEnabled(bool enabled)
        {
            VariantModeComboBox.IsEnabled = enabled;
            bool custom = enabled && VariantMode == "Custom";
            VariantNumberBox.IsEnabled = custom;
            VariantPayloadKindComboBox.IsEnabled = custom;
            VariantPayloadNumberBox.IsEnabled = custom && PayloadKind != "None";
        }

        private void VariantModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateVisibility();
        }

        private void VariantPayloadKindComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (VariantNumberBox == null || VariantPayloadNumberBox == null || VariantModeComboBox == null || VariantPayloadKindComboBox == null)
                return;

            bool custom = VariantMode == "Custom";
            VariantNumberLabel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            VariantNumberPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;

            bool showPayload = custom && PayloadKind != "None";
            VariantPayloadNumberBox.Visibility = showPayload ? Visibility.Visible : Visibility.Collapsed;
            VariantPayloadLabel.Visibility = showPayload ? Visibility.Visible : Visibility.Collapsed;
            VariantPayloadNumberBox.IsEnabled = showPayload;
            if (!showPayload)
                VariantPayloadNumberBox.Value = double.NaN;
            if (!custom)
            {
                VariantNumberBox.Value = double.NaN;
                VariantPayloadKindComboBox.SelectedIndex = 0;
            }
        }

        private static bool TryParseUInt32(string text, out uint value)
        {
            if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            if (!string.IsNullOrWhiteSpace(text) && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

            value = 0;
            return false;
        }

        private static string FormatUInt32(double number)
        {
            if (double.IsNaN(number) || number < 0 || number > uint.MaxValue || number != Math.Truncate(number))
                return "";
            return ((uint)number).ToString(CultureInfo.InvariantCulture);
        }
    }
}
