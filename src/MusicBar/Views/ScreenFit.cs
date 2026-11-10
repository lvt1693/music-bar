using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MusicBar.Interop;

namespace MusicBar.Views;

/// <summary>
/// Keeps a window inside the work area (the screen minus the taskbar) of the monitor it is on, so its
/// title bar and buttons can always be reached. It runs when the window opens and again when the work
/// area changes (taskbar moved or resized, resolution changed) or the window lands on a screen with
/// another scaling. A window that already fits is left exactly as it is.
/// A resizable window is shrunk (and its minimum size lowered if the screen is smaller than that);
/// a fixed-size window with a drawn title bar has its content scaled down instead, because its layout
/// is made for one size.
/// </summary>
internal sealed class ScreenFit
{
    const int WM_SETTINGCHANGE = 0x001A, WM_DISPLAYCHANGE = 0x007E, SPI_SETWORKAREA = 0x002F;
    const int WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232, DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    // Room left under a scaled window for the recap's note to wrap onto a second line; the window cannot grow by itself then.
    const double SpareHeight = 16;

    // The least a fixed-size window is scaled to, so a tiny work area cannot shrink it to nothing.
    const double LeastScale = 0.4;

    readonly Window _window;
    readonly bool _scaleContent;
    readonly double _minWidth, _minHeight;
    Size _design;
    bool _scaled, _queued, _moving, _due;

    ScreenFit(Window window, bool scaleContent, Size design)
    {
        _window = window;
        _scaleContent = scaleContent;
        _design = design;
        _minWidth = window.MinWidth;
        _minHeight = window.MinHeight;
    }

    /// <summary>For a resizable window: shrink to the work area and lower its minimum size if need be.</summary>
    public static void Attach(Window window) => new ScreenFit(window, false, default).Hook();

    /// <summary>
    /// For a fixed-size window: scale the whole content down to fit. <paramref name="design"/> is the
    /// window's size at 100 percent in DIPs, margins included; left out, it is measured from the content.
    /// </summary>
    public static void AttachScaled(Window window, Size? design = null) => new ScreenFit(window, true, design ?? default).Hook();

    void Hook()
    {
        _window.SourceInitialized += (_, _) =>
        {
            Fit(opening: true);
            HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle)?.AddHook(OnMessage);
        };
        _window.DpiChanged += (_, _) => Queue();
        _window.StateChanged += (_, _) =>
        {
            if (_window.WindowState == WindowState.Normal) Queue();
        };
    }

    IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE || msg == WM_SETTINGCHANGE && wParam.ToInt64() == SPI_SETWORKAREA) Queue();
        else if (msg == WM_ENTERSIZEMOVE) _moving = true;
        else if (msg == WM_EXITSIZEMOVE)
        {
            _moving = false;
            if (_due) Queue();
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// A moment later, once Windows (and WPF, after a scaling change) has finished placing the window,
    /// and once for a burst of messages.
    /// </summary>
    void Queue()
    {
        // While the user drags the window (halfway onto a screen with another scaling, say) moving it
        // would make it jump; the fit waits for the drop.
        if (_moving)
        {
            _due = true;
            return;
        }
        if (_queued) return;
        _queued = true;
        _window.Dispatcher.InvokeAsync(() =>
        {
            _queued = false;
            if (_moving)
            {
                _due = true;
                return;
            }
            _due = false;
            Fit(opening: false);
        }, DispatcherPriority.Background);
    }

    void Fit(bool opening)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero || _window.WindowState != WindowState.Normal) return;
        var monitor = Monitors.Get(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST));
        if (monitor == null || !Native.GetWindowRect(hwnd, out var now)) return;

        // Everything below is in physical pixels, the one measure that means the same on every screen.
        var work = monitor.WorkArea;
        double scale = monitor.Scale;
        int width = now.Width, height = now.Height;

        // A window with a standard frame has invisible resize borders inside its rectangle; what must
        // fit is the visible frame, so a window the user snapped to a screen edge is not nudged.
        int overL = 0, overT = 0, overR = 0, overB = 0;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT frame, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) == 0)
        {
            overL = Math.Max(0, frame.Left - now.Left);
            overT = Math.Max(0, frame.Top - now.Top);
            overR = Math.Max(0, now.Right - frame.Right);
            overB = Math.Max(0, now.Bottom - frame.Bottom);
        }
        int overX = overL + overR, overY = overT + overB;

        if (_scaleContent)
        {
            if (_window.Content is not FrameworkElement root) return;
            if (_design.Width <= 0 || _design.Height <= 0)
            {
                root.LayoutTransform = Transform.Identity;
                root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _design = new Size(root.DesiredSize.Width, root.DesiredSize.Height + SpareHeight);
            }

            // The margin around the content (room for a shadow) is not scaled, only what is inside it.
            double marginX = root.Margin.Left + root.Margin.Right, marginY = root.Margin.Top + root.Margin.Bottom;
            double fit = Math.Min((work.Width / scale - marginX) / (_design.Width - marginX),
                                  (work.Height / scale - marginY) / (_design.Height - marginY));
            fit = Math.Clamp(fit, LeastScale, 1);
            if (fit < 1 || _scaled)
            {
                _scaled = fit < 1;
                root.LayoutTransform = _scaled ? new ScaleTransform(fit, fit) : Transform.Identity;
                // A window that sizes itself to its content would fight the size set below.
                _window.SizeToContent = SizeToContent.Manual;
                width = Math.Min(work.Width, (int)Math.Ceiling(((_design.Width - marginX) * fit + marginX) * scale));
                height = Math.Min(work.Height, (int)Math.Ceiling(((_design.Height - marginY) * fit + marginY) * scale));
            }
        }
        else
        {
            // Minimum sizes first: a window cannot be made smaller than they say.
            _window.MinWidth = Math.Min(_minWidth, Math.Floor(work.Width / scale));
            _window.MinHeight = Math.Min(_minHeight, Math.Floor(work.Height / scale));
            width = Math.Min(width - overX, work.Width) + overX;
            height = Math.Min(height - overY, work.Height) + overY;
        }

        // From here on the visible frame is what is placed: the invisible borders may hang over the edge.
        int shownW = width - overX, shownH = height - overY;
        int left = now.Left + overL, top = now.Top + overT;
        if (opening && _window.WindowStartupLocation == WindowStartupLocation.CenterScreen)
        {
            // It was centered for its old size; a smaller one is centered again, on the axes that shrank.
            if (width != now.Width) left = work.Left + (work.Width - shownW) / 2;
            if (height != now.Height) top = work.Top + (work.Height - shownH) / 2;
        }
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - shownW)) - overL;
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - shownH)) - overT;

        if (left == now.Left && top == now.Top && width == now.Width && height == now.Height) return;
        Native.SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Native.RECT value, int size);
}
