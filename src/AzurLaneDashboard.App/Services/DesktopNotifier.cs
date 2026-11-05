using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AzurLaneDashboard.App.Services;

/// <summary>
/// Windows notifications through a notification-area icon (Shell_NotifyIcon). Windows 10 and 11 show the balloon
/// as a toast and keep it in the notification center. The icon is added with the first notification and stays
/// until the window closes, so the notice can still be clicked later. Clicking either brings the window back.
/// </summary>
public sealed class DesktopNotifier : IDisposable
{
    private const int CallbackMessage = 0x8000 + 0x4E; // WM_APP + n
    private const int IconId = 1;
    private const int AddIcon = 0x0;
    private const int ModifyIcon = 0x1;
    private const int DeleteIcon = 0x2;
    private const int SetVersion = 0x4;
    private const int Version4 = 4;
    private const int MessageFlag = 0x01;
    private const int IconFlag = 0x02;
    private const int TipFlag = 0x04;
    private const int InfoFlag = 0x10;
    private const int ShowTipFlag = 0x80;
    private const int WarningInfo = 0x02;
    private const int UserInfo = 0x04;
    private const int LargeIconInfo = 0x20;
    private const int Select = 0x400;
    private const int KeySelect = 0x401;
    private const int BalloonUserClick = 0x405;
    private const uint FlashAll = 0x3;
    private const uint FlashUntilForeground = 0xC;

    private readonly Window window;
    private readonly string tip;
    private readonly Action clicked;
    private HwndSource? source;
    private IntPtr smallIcon;
    private IntPtr largeIcon;
    private bool added;

    public DesktopNotifier(Window window, string tip, Action clicked)
    {
        this.window = window;
        this.tip = tip;
        this.clicked = clicked;
    }

    /// <summary>A warning shows the system warning glyph; anything else shows the app icon.</summary>
    public void Show(string title, string text, bool warning)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (source is null)
        {
            source = HwndSource.FromHwnd(handle);
            source?.AddHook(Hook);
        }

        if (smallIcon == IntPtr.Zero) smallIcon = LoadAppIcon(GetSystemMetrics(49)); // SM_CXSMICON
        if (largeIcon == IntPtr.Zero) largeIcon = LoadAppIcon(GetSystemMetrics(11)); // SM_CXICON

        var data = Data(handle);
        data.uFlags = InfoFlag;
        data.szInfoTitle = Clip(title, 63);
        data.szInfo = Clip(text, 255);
        data.dwInfoFlags = warning ? WarningInfo : UserInfo | LargeIconInfo;
        data.hBalloonIcon = largeIcon;
        // A failed modify means Explorer restarted and dropped the icon; add it again once.
        if (!(EnsureIcon(handle) && Shell_NotifyIcon(ModifyIcon, ref data)))
        {
            added = false;
            if (EnsureIcon(handle)) Shell_NotifyIcon(ModifyIcon, ref data);
        }

        if (!window.IsActive) Flash(handle);
    }

    private bool EnsureIcon(IntPtr handle)
    {
        if (added) return true;
        var data = Data(handle);
        data.uFlags = MessageFlag | IconFlag | TipFlag | ShowTipFlag;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = smallIcon;
        data.szTip = Clip(tip, 127);
        added = Shell_NotifyIcon(AddIcon, ref data);
        if (added)
        {
            data.uTimeoutOrVersion = Version4;
            Shell_NotifyIcon(SetVersion, ref data);
        }

        return added;
    }

    private static NotifyIconData Data(IntPtr handle) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = handle,
        uID = IconId,
        szTip = "",
        szInfo = "",
        szInfoTitle = ""
    };

    // Version 4 puts the event in the low word of lParam: a click on the toast, or on the icon itself.
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != CallbackMessage) return IntPtr.Zero;
        if ((int)(lParam.ToInt64() & 0xFFFF) is Select or KeySelect or BalloonUserClick)
        {
            clicked();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void Flash(IntPtr handle)
    {
        var info = new FlashInfo
        {
            cbSize = (uint)Marshal.SizeOf<FlashInfo>(),
            hwnd = handle,
            dwFlags = FlashAll | FlashUntilForeground
        };
        FlashWindowEx(ref info);
    }

    // The frame of Assets/app.ico nearest the size at or above it; every frame is PNG, which Windows reads as is.
    private static IntPtr LoadAppIcon(int size)
    {
        if (size <= 0) return IntPtr.Zero;
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/AzurLaneDashboard.App;component/Assets/app.ico"));
            if (resource is null) return IntPtr.Zero;
            using var stream = resource.Stream;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            int best = -1, bestEdge = 0;
            for (var index = 0; index < BitConverter.ToUInt16(bytes, 4); index++)
            {
                var edge = bytes[6 + 16 * index] is 0 ? 256 : bytes[6 + 16 * index];
                var better = best < 0 ||
                             (edge >= size ? bestEdge < size || edge < bestEdge : bestEdge < size && edge > bestEdge);
                if (better) (best, bestEdge) = (index, edge);
            }

            if (best < 0) return IntPtr.Zero;
            var entry = 6 + 16 * best;
            var image = bytes.AsSpan(BitConverter.ToInt32(bytes, entry + 12), BitConverter.ToInt32(bytes, entry + 8)).ToArray();
            return CreateIconFromResourceEx(image, image.Length, true, 0x00030000, size, size, 0);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException)
        {
            return IntPtr.Zero;
        }
    }

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    public void Dispose()
    {
        if (added)
        {
            var data = Data(new WindowInteropHelper(window).Handle);
            Shell_NotifyIcon(DeleteIcon, ref data);
            added = false;
        }

        source?.RemoveHook(Hook);
        source = null;
        if (smallIcon != IntPtr.Zero) DestroyIcon(smallIcon);
        if (largeIcon != IntPtr.Zero) DestroyIcon(largeIcon);
        smallIcon = largeIcon = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(byte[] bits, int size, [MarshalAs(UnmanagedType.Bool)] bool icon,
        int version, int width, int height, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
