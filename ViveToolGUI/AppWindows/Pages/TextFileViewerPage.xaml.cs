using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Collections.ObjectModel;
using ViVeToolGUI.AppWindows;

namespace ViVeToolGUI.AppWindows.Pages;

public sealed partial class TextFileViewerPage : Page
{
    internal TextFileViewerWindow? Owner { get; set; }

    public ObservableCollection<FeatureEntry> FilteredItems { get; } = new();

    public InfoBar StatusInfoBarControl => StatusInfoBar;
    public TranslateTransform InfoBarTransformControl => InfoBarTransform;
    public Microsoft.UI.Xaml.Controls.Tabular.TableView FeaturesTableViewControl => FeaturesTableView;
    public Border LoadingOverlayControl => LoadingOverlay;
    public TextBlock LoadingTextControl => LoadingText;
    public Storyboard InfoBarSlideInControl => InfoBarSlideIn;
    public Storyboard InfoBarSlideOutControl => InfoBarSlideOut;

    public TextFileViewerPage()
    {
        InitializeComponent();
    }

    private void FeatureRowMenuFlyout_Opening(object sender, object e)
        => Owner?.FeatureRowMenuFlyout_Opening(sender, e);

    private void CmdEnable_Click(object sender, RoutedEventArgs e)
        => Owner?.CmdEnable_Click(sender, e);

    private void CmdDisable_Click(object sender, RoutedEventArgs e)
        => Owner?.CmdDisable_Click(sender, e);

    private void CmdRestore_Click(object sender, RoutedEventArgs e)
        => Owner?.CmdRestore_Click(sender, e);

    private void CmdCopy_Click(object sender, RoutedEventArgs e)
        => Owner?.CmdCopy_Click(sender, e);

    private void CmdSelectAll_Click(object sender, RoutedEventArgs e)
        => Owner?.CmdSelectAll_Click(sender, e);
}
