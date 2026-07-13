using Clicky.Windows.Native;
using Clicky.Windows.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clicky.Windows.Views;

public partial class AgentFileApprovalWindow : Window
{
    public AgentFileApprovalWindow(
        string workspacePath,
        AgentArtifactPackage package,
        IReadOnlyList<AgentFilePlan> plans)
    {
        InitializeComponent();
        SourceInitialized += ExcludeFromCapture;
        SummaryText.Text = package.Summary;
        WorkspaceText.Text = $"Workspace: {workspacePath}";
        FileList.ItemsSource = plans;
        if (plans.Count > 0)
        {
            FileList.SelectedIndex = 0;
        }
        Loaded += async (_, _) =>
        {
            if (!NativeMethods.IsVisualTest)
            {
                return;
            }
            await Task.Delay(350);
            WriteVisualTestSnapshot();
        };
    }

    public bool Approved { get; private set; }

    private void FileSelected(object sender, SelectionChangedEventArgs eventArgs)
    {
        PreviewText.Text = FileList.SelectedItem is AgentFilePlan plan ? plan.Content : string.Empty;
    }

    private void ReviewChanged(object sender, RoutedEventArgs eventArgs)
    {
        ApproveButton.IsEnabled = ReviewCheck.IsChecked == true;
    }

    private void Approve(object sender, RoutedEventArgs eventArgs)
    {
        Approved = ReviewCheck.IsChecked == true;
        DialogResult = Approved;
        Close();
    }

    private void Cancel(object sender, RoutedEventArgs eventArgs) => Close();

    private void ExcludeFromCapture(object? sender, EventArgs eventArgs)
    {
        if (NativeMethods.IsVisualTest)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        _ = NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture);
    }

    private void WriteVisualTestSnapshot()
    {
        if (Content is not Visual visual)
        {
            return;
        }

        UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Path.GetTempPath(), "clicky-agent-approval-render.png"));
        encoder.Save(stream);
    }
}
