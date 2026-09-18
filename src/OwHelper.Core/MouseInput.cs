using System;

namespace OwHelper.Core;

public static class MouseInput
{
    public const int VkLeftButton = 0x01;
    public const int VkRightButton = 0x02;
    public const int VkMiddleButton = 0x04;
    public const int VkXButton1 = 0x05;
    public const int VkXButton2 = 0x06;

    public static bool IsMouseVirtualKey(int vk)
        => vk is VkLeftButton or VkRightButton or VkMiddleButton or VkXButton1 or VkXButton2;

    public static bool TryGetClientCenter(IntPtr hwnd, out int x, out int y)
    {
        if (Native.GetClientRect(hwnd, out Native.RECT rect))
        {
            x = (rect.Right - rect.Left) / 2;
            y = (rect.Bottom - rect.Top) / 2;
            return true;
        }
        x = 0;
        y = 0;
        return false;
    }

    public static bool SendMove(IntPtr hwnd, int x, int y, int state = 0)
        => Native.PostMessage(hwnd, Native.WM_MOUSEMOVE, new IntPtr(state), PointLParam(x, y));

    public static bool SendButton(IntPtr hwnd, int vk, bool down, int x, int y)
        => SendButton(hwnd, vk, down, x, y, down ? ButtonBit(vk) : 0);

    public static bool SendButton(IntPtr hwnd, int vk, bool down, int x, int y, int state)
    {
        if (!TryMap(vk, down, state, out uint message, out IntPtr wParam)) return false;
        return Native.PostMessage(hwnd, message, wParam, PointLParam(x, y));
    }

    static IntPtr PointLParam(int x, int y)
        => new IntPtr((y << 16) | (x & 0xFFFF));

    static int ButtonBit(int vk) => vk switch
    {
        VkLeftButton => Native.MK_LBUTTON,
        VkRightButton => Native.MK_RBUTTON,
        VkMiddleButton => Native.MK_MBUTTON,
        0x10 or 0xA0 or 0xA1 => Native.MK_SHIFT,
        0x11 or 0xA2 or 0xA3 => Native.MK_CONTROL,
        _ => 0,
    };

    internal static int ApplyKeyDown(int state, int vk) => state | ButtonBit(vk);

    internal static int ApplyKeyUp(int state, int vk) => state & ~ButtonBit(vk);

    static bool TryMap(int vk, bool down, int state, out uint message, out IntPtr wParam)
    {
        switch (vk)
        {
            case VkLeftButton:
                message = down ? Native.WM_LBUTTONDOWN : Native.WM_LBUTTONUP;
                wParam = new IntPtr(state);
                return true;
            case VkRightButton:
                message = down ? Native.WM_RBUTTONDOWN : Native.WM_RBUTTONUP;
                wParam = new IntPtr(state);
                return true;
            case VkMiddleButton:
                message = down ? Native.WM_MBUTTONDOWN : Native.WM_MBUTTONUP;
                wParam = new IntPtr(state);
                return true;
            case VkXButton1:
            case VkXButton2:
                message = down ? Native.WM_XBUTTONDOWN : Native.WM_XBUTTONUP;
                int button = vk == VkXButton1 ? Native.XBUTTON1 : Native.XBUTTON2;
                wParam = new IntPtr((state & 0xFFFF) | (button << 16));
                return true;
            default:
                message = 0;
                wParam = IntPtr.Zero;
                return false;
        }
    }
}
