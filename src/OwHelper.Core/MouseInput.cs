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

    public static bool SendMove(IntPtr hwnd, int x, int y)
        => Native.PostMessage(hwnd, Native.WM_MOUSEMOVE, IntPtr.Zero, PointLParam(x, y));

    public static bool SendButton(IntPtr hwnd, int vk, bool down, int x, int y)
    {
        if (!TryMap(vk, down, out uint message, out IntPtr wParam)) return false;
        return Native.PostMessage(hwnd, message, wParam, PointLParam(x, y));
    }

    static IntPtr PointLParam(int x, int y)
        => new IntPtr((y << 16) | (x & 0xFFFF));

    static bool TryMap(int vk, bool down, out uint message, out IntPtr wParam)
    {
        switch (vk)
        {
            case VkLeftButton:
                message = down ? Native.WM_LBUTTONDOWN : Native.WM_LBUTTONUP;
                wParam = new IntPtr(Native.MK_LBUTTON);
                return true;
            case VkRightButton:
                message = down ? Native.WM_RBUTTONDOWN : Native.WM_RBUTTONUP;
                wParam = new IntPtr(Native.MK_RBUTTON);
                return true;
            case VkMiddleButton:
                message = down ? Native.WM_MBUTTONDOWN : Native.WM_MBUTTONUP;
                wParam = new IntPtr(Native.MK_MBUTTON);
                return true;
            case VkXButton1:
            case VkXButton2:
                message = down ? Native.WM_XBUTTONDOWN : Native.WM_XBUTTONUP;
                int button = vk == VkXButton1 ? Native.XBUTTON1 : Native.XBUTTON2;
                wParam = new IntPtr(button << 16);
                return true;
            default:
                message = 0;
                wParam = IntPtr.Zero;
                return false;
        }
    }
}
