using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MusicBar.Interop;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// The part of a skin that rises above the taskbar: a skin saying "//! headroom: N" (fire off a sword lying on the
/// taskbar, sparks, a beam from above) draws up to N DIPs onto the desktop here. It is see-through and click-through,
/// fed every frame from the bar's own engine, so both halves keep the same time, sound and colours; the overlay
/// shows it only while nothing but the desktop is in that strip (never over a window, a game, the Start menu).
/// </summary>
public sealed class TaskbarHeadroomWindow : Window
{
    readonly SkinView _view = new();

    public TaskbarHeadroomWindow()
    {
        Title = "MusicBar Headroom";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Left = Top = -20000;
        Width = 400;
        Height = 100;
        UseLayoutRounding = true;
        Content = _view;
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT,
                Native.WS_EX_APPWINDOW);
            CpuRendering.Set(this, SkinLibrary.IsDirect3D11(_view.Skin?.Bytecode));
        };
    }

    public IntPtr Hwnd { get; private set; }

    /// <summary>
    /// The skin to draw, the taskbar's thickness and the headroom's (DIPs), and whether the headroom is above the
    /// taskbar (a taskbar at the bottom) or below it (at the top).
    /// </summary>
    public void Configure(SkinInfo skin, double barHeight, double headroom, bool above)
    {
        _view.SetSkin(skin);
        _view.SetHeadroom(headroom, isHeadroom: true, barHeight, above);
        // A WPF effect runs only on the graphics card; a Direct3D 11 skin arrives as a bitmap, fine on the CPU.
        CpuRendering.Set(this, SkinLibrary.IsDirect3D11(skin.Bytecode));
    }

    /// <summary>One frame, from the engine that keeps the bar's clock, audio and colours.</summary>
    public void Update(SeekRenderer engine) => _view.Update(engine);
}
