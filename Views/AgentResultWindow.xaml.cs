using Clicky.Windows.Native;
using Clicky.Windows.Services;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clicky.Windows.Views;

public partial class AgentResultWindow : Window
{
    private readonly AgentTaskResult _result;

    public AgentResultWindow(AgentTaskResult result)
    {
        _result = result;
        InitializeComponent();
        SourceInitialized += ExcludeFromCapture;
        TaskText.Text = result.Task;
        ProviderText.Text = $"Completed via {result.ProviderName}";
        CompletedText.Text = result.CompletedAt.ToLocalTime().ToString("g");
        ResultText.Text = result.Text;
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

    private void CopyResult(object sender, RoutedEventArgs eventArgs)
    {
        System.Windows.Clipboard.SetText(_result.Text);
    }

    private void CloseWindow(object sender, RoutedEventArgs eventArgs) => Close();

    private void ExcludeFromCapture(object? sender, EventArgs eventArgs)
    {
        if (NativeMethods.IsVisualTest)
        {
            return;
        }
        _ = NativeMethods.SetWindowDisplayAffinity(
            new WindowInteropHelper(this).Handle,
            NativeMethods.WdaExcludeFromCapture);
    }

    private void WriteVisualTestSnapshot()
    {
        if (Content is not Visual visual)
        {
            return;
        }
        UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Path.GetTempPath(), "clicky-agent-result-render.png"));
        encoder.Save(stream);
    }
}
