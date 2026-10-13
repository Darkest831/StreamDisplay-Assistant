using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace StreamDisplayAssistant.Native;

/// <summary>
/// 100% Native Windows Global Hotkey manager via Win32 RegisterHotKey.
/// Provides emergency system-wide hotkeys:
/// - Ctrl + Shift + F12: Emergency Restore Native Resolution
/// - Ctrl + Shift + F11: Toggle OLED Blackout Shield
/// - Ctrl + Shift + H: Toggle Windows HDR
/// - Ctrl + Shift + M: Toggle Master Audio Mute
/// Zero third-party dependencies.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    // Modifiers
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    // Virtual Keys
    private const uint VK_F11 = 0x7A;
    private const uint VK_F12 = 0x7B;
    private const uint VK_H = 0x48;
    private const uint VK_M = 0x4D;

    // Hotkey IDs
    public const int ID_HOTKEY_RESTORE_NATIVE = 9001;
    public const int ID_HOTKEY_TOGGLE_BLACKOUT = 9002;
    public const int ID_HOTKEY_TOGGLE_HDR = 9003;
    public const int ID_HOTKEY_TOGGLE_MUTE = 9004;

    private IntPtr _windowHandle = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private HwndSourceHook? _hook;
    private bool _isRegistered = false;

    public event Action? OnEmergencyRestoreNativeRequested;
    public event Action? OnToggleBlackoutRequested;
    public event Action? OnToggleHdrRequested;
    public event Action? OnToggleMuteRequested;
    public event Action<string>? LogMessage;

    public void RegisterHotkeys(IntPtr hWnd)
    {
        if (_isRegistered || hWnd == IntPtr.Zero) return;

        _windowHandle = hWnd;
        _hwndSource = HwndSource.FromHwnd(hWnd);
        _hook = new HwndSourceHook(HwndHook);
        _hwndSource?.AddHook(_hook);

        uint mods = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;

        bool k1 = RegisterHotKey(hWnd, ID_HOTKEY_RESTORE_NATIVE, mods, VK_F12);
        bool k2 = RegisterHotKey(hWnd, ID_HOTKEY_TOGGLE_BLACKOUT, mods, VK_F11);
        bool k3 = RegisterHotKey(hWnd, ID_HOTKEY_TOGGLE_HDR, mods, VK_H);
        bool k4 = RegisterHotKey(hWnd, ID_HOTKEY_TOGGLE_MUTE, mods, VK_M);

        _isRegistered = true;
        LogMessage?.Invoke($"Global Hotkeys armed: Ctrl+Shift+F12 (Restore), Ctrl+Shift+F11 (Blackout), Ctrl+Shift+H (HDR), Ctrl+Shift+M (Mute).");
    }

    public void UnregisterHotkeys()
    {
        if (!_isRegistered || _windowHandle == IntPtr.Zero) return;

        try
        {
            UnregisterHotKey(_windowHandle, ID_HOTKEY_RESTORE_NATIVE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TOGGLE_BLACKOUT);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TOGGLE_HDR);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TOGGLE_MUTE);

            if (_hook != null)
            {
                _hwndSource?.RemoveHook(_hook);
                _hook = null;
            }
        }
        catch { }
        finally
        {
            _isRegistered = false;
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            switch (hotkeyId)
            {
                case ID_HOTKEY_RESTORE_NATIVE:
                    OnEmergencyRestoreNativeRequested?.Invoke();
                    handled = true;
                    break;
                case ID_HOTKEY_TOGGLE_BLACKOUT:
                    OnToggleBlackoutRequested?.Invoke();
                    handled = true;
                    break;
                case ID_HOTKEY_TOGGLE_HDR:
                    OnToggleHdrRequested?.Invoke();
                    handled = true;
                    break;
                case ID_HOTKEY_TOGGLE_MUTE:
                    OnToggleMuteRequested?.Invoke();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotkeys();
    }
}
