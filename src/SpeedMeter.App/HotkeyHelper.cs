using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SpeedMeter.App;

public sealed class HotkeyHelper : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    private const int HotkeyIdDashboard = 9001;
    private const int HotkeyIdWidget = 9002;
    private const int HotkeyIdClickThrough = 9003;

    private const uint VK_S = 0x53;
    private const uint VK_W = 0x57;
    private const uint VK_C = 0x43;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private readonly Window window;
    private readonly Action onDashboard;
    private readonly Action onToggleWidget;
    private readonly Action onToggleClickThrough;
    private HwndSource? source;
    private nint handle;
    private bool registered;

    public HotkeyHelper(
        Window window,
        Action onDashboard,
        Action onToggleWidget,
        Action onToggleClickThrough)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        this.onDashboard = onDashboard ?? throw new ArgumentNullException(nameof(onDashboard));
        this.onToggleWidget = onToggleWidget ?? throw new ArgumentNullException(nameof(onToggleWidget));
        this.onToggleClickThrough = onToggleClickThrough ?? throw new ArgumentNullException(nameof(onToggleClickThrough));

        if (window.IsLoaded)
        {
            Initialize();
        }
        else
        {
            window.Loaded += (_, _) => Initialize();
        }
    }

    private void Initialize()
    {
        if (registered) return;
        handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;

        source = HwndSource.FromHwnd(handle);
        source?.AddHook(HwndHook);

        var mods = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
        _ = RegisterHotKey(handle, HotkeyIdDashboard, mods, VK_S);
        _ = RegisterHotKey(handle, HotkeyIdWidget, mods, VK_W);
        _ = RegisterHotKey(handle, HotkeyIdClickThrough, mods, VK_C);
        registered = true;
    }

    private nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == HotkeyIdDashboard)
            {
                onDashboard();
                handled = true;
            }
            else if (id == HotkeyIdWidget)
            {
                onToggleWidget();
                handled = true;
            }
            else if (id == HotkeyIdClickThrough)
            {
                onToggleClickThrough();
                handled = true;
            }
        }
        return 0;
    }

    public void Dispose()
    {
        if (!registered || handle == 0) return;
        registered = false;
        try
        {
            UnregisterHotKey(handle, HotkeyIdDashboard);
            UnregisterHotKey(handle, HotkeyIdWidget);
            UnregisterHotKey(handle, HotkeyIdClickThrough);
        }
        catch { }

        source?.RemoveHook(HwndHook);
    }
}
