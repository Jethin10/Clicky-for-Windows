using Clicky.Windows.Native;
using Clicky.Windows.Services;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clicky.Windows.Views;

public partial class EmailApprovalWindow : Window
{
    public EmailApprovalWindow(EmailSettings settings, AgentEmailProposal proposal)
    {
        InitializeComponent();
        SourceInitialized += ExcludeFromCapture;
        FromText.Text = string.IsNullOrWhiteSpace(settings.FromName)
            ? settings.FromAddress
            : $"{settings.FromName} <{settings.FromAddress}>";
        RecipientsText.Text = $"To: {string.Join(", ", proposal.To)}"
            + (proposal.Cc.Count > 0 ? $"\nCc: {string.Join(", ", proposal.Cc)}" : string.Empty);
        SubjectText.Text = proposal.Subject;
        BodyText.Text = proposal.Body;
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

    private void ConfirmationChanged(object sender, RoutedEventArgs eventArgs)
    {
        SendButton.IsEnabled = ConfirmCheck.IsChecked == true;
    }

    private void Send(object sender, RoutedEventArgs eventArgs)
    {
        Approved = ConfirmCheck.IsChecked == true;
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
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Path.GetTempPath(), "clicky-email-approval-render.png"));
        encoder.Save(stream);
    }
}
