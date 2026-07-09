using System;
using System.Runtime.InteropServices;

namespace Clicky.Windows.Native;

internal static class NativeMethods
{
    internal const int GwlExStyle = -20;
    internal const long WsExLayered = 0x00080000L;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExNoActivate = 0x08000000L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const uint WdaExcludeFromCapture = 0x00000011;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr windowHandle, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    internal static bool IsKeyDown(int virtualKey) => GetAsyncKeyState(virtualKey) < 0;

    internal static bool IsVisualTest => string.Equals(
        Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST"),
        "1",
        StringComparison.Ordinal);

    internal static void ConfigureCompanionOverlay(IntPtr windowHandle)
    {
        var existing = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
        var styles = existing | WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow;
        _ = SetWindowLongPtr(windowHandle, GwlExStyle, new IntPtr(styles));
        if (!IsVisualTest)
        {
            _ = SetWindowDisplayAffinity(windowHandle, WdaExcludeFromCapture);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    internal int X;
    internal int Y;
}
