using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AzurLaneDashboard.App.Services;

/// <summary>
/// A system-wide key (RegisterHotKey) delivered to this window even while the game window has focus. Registered
/// only while needed so the key stays free for other programs the rest of the time.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const uint NoRepeat = 0x4000;
    private const int Id = 0x4D41;

    private readonly Window window;
    private readonly uint virtualKey;
    private readonly Action pressed;
    private HwndSource? source;
    private bool registered;

    public GlobalHotkey(Window window, uint virtualKey, Action pressed)
    {
        this.window = window;
        this.virtualKey = virtualKey;
        this.pressed = pressed;
    }

    /// <summary>False when another program already holds the key.</summary>
    public bool Register()
    {
        if (registered) return true;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        source ??= HwndSource.FromHwnd(handle);
        source?.AddHook(Hook);
        registered = RegisterHotKey(handle, Id, NoRepeat, virtualKey);
        if (!registered) source?.RemoveHook(Hook);
        return registered;
    }

    public void Unregister()
    {
        if (!registered) return;
        UnregisterHotKey(new WindowInteropHelper(window).Handle, Id);
        source?.RemoveHook(Hook);
        registered = false;
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == HotkeyMessage && wParam.ToInt32() == Id)
        {
            pressed();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose() => Unregister();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
