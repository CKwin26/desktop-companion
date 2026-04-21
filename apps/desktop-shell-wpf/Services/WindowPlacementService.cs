using System.Windows;
using System.Windows.Media;

namespace DesktopCompanion.WpfHost.Services;

public static class WindowPlacementService
{
    public static void SnapToBottomRight(Window window, double rightMargin = 26, double bottomMargin = 34)
    {
        var workArea = SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        var workAreaLeft = workArea.Left / dpi.DpiScaleX;
        var workAreaTop = workArea.Top / dpi.DpiScaleY;
        var workAreaWidth = workArea.Width / dpi.DpiScaleX;
        var workAreaHeight = workArea.Height / dpi.DpiScaleY;

        window.Left = workAreaLeft + workAreaWidth - window.Width - rightMargin;
        window.Top = workAreaTop + workAreaHeight - window.Height - bottomMargin;
    }
}

internal static class NativeWindowMethods
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    internal static bool TryGetWindowRect(IntPtr hwnd, out Rect rect)
    {
        if (hwnd == IntPtr.Zero)
        {
            rect = default;
            return false;
        }

        return GetWindowRect(hwnd, out rect);
    }
}
