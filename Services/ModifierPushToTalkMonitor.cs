using Clicky.Windows.Native;

namespace Clicky.Windows.Services;

/// <summary>
/// Non-consuming, system-wide Ctrl+Alt hold monitor.  This mirrors the upstream
/// modifier-only workflow without registering an intrusive Windows shortcut.
/// </summary>
public sealed class ModifierPushToTalkMonitor : IDisposable
{
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;

    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _thread;
    private bool _previouslyHolding;

    public event Action? Pressed;
    public event Action? Released;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Poll)
        {
            IsBackground = true,
            Name = "Clicky Ctrl+Alt monitor"
        };
        _thread.Start();
    }

    private void Poll()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            var isHolding = NativeMethods.IsKeyDown(VkControl) && NativeMethods.IsKeyDown(VkMenu);

            if (isHolding && !_previouslyHolding)
            {
                _previouslyHolding = true;
                Pressed?.Invoke();
            }
            else if (!isHolding && _previouslyHolding)
            {
                _previouslyHolding = false;
                Released?.Invoke();
            }

            Thread.Sleep(16);
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _thread?.Join(millisecondsTimeout: 250);
        _cancellation.Dispose();
    }
}
