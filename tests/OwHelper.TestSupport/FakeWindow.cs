using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace OwHelper.TestSupport;

public sealed class FakeWindow : IDisposable
{
    const int HWND_MESSAGE = -3;
    const uint WM_CLOSE = 0x0010;
    const uint WM_DESTROY = 0x0002;
    const uint WS_POPUP = 0x80000000;

    const uint WM_USER_READY = 0x0400 + 42;
    const uint WM_DWMCOMPOSITIONCHANGED = 0x031F;
    const uint WM_WINDOWPOSCHANGING = 0x0046;
    const uint WM_WINDOWPOSCHANGED = 0x0047;
    const uint WM_NCCALCSIZE = 0x0083;

    public readonly record struct Record(uint Msg, IntPtr WParam, IntPtr LParam);

    readonly Thread thread;
    readonly List<Record> records = new();
    readonly ManualResetEventSlim ready = new(false);
    readonly string className = "FakeWindow_" + Guid.NewGuid().ToString("N");
    readonly WndProcDelegate wndProc;
    readonly bool topLevel;
    volatile bool created;
    int disposedFlag;
    IntPtr handle = IntPtr.Zero;

    public IntPtr Handle => handle;

    public FakeWindow(bool topLevel = false)
    {
        this.topLevel = topLevel;
        wndProc = WndProc;
        thread = new Thread(ThreadMain) { IsBackground = true };
        thread.Start();
        ready.Wait(5000);
    }

    void ThreadMain()
    {
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className,
        };
        RegisterClassEx(ref wc);
        IntPtr parent = topLevel ? IntPtr.Zero : new IntPtr(HWND_MESSAGE);
        uint style = topLevel ? WS_POPUP : 0;
        uint width = topLevel ? 4000u : 100u;
        uint height = topLevel ? 3000u : 100u;
        handle = CreateWindowEx(0, className, className, style, 0, 0, (int)width, (int)height, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        created = true;
        PostMessage(handle, WM_USER_READY, IntPtr.Zero, IntPtr.Zero);
        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            DispatchMessage(ref msg);
        }
    }

    IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CLOSE)
        {
            DestroyWindow(hWnd);
            return IntPtr.Zero;
        }
        if (msg == WM_DESTROY)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        if (msg == WM_USER_READY)
        {
            lock (records) records.Clear();
            ready.Set();
            return IntPtr.Zero;
        }
        if (msg is WM_DWMCOMPOSITIONCHANGED or WM_WINDOWPOSCHANGING or WM_WINDOWPOSCHANGED or WM_NCCALCSIZE)
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }
        lock (records) records.Add(new Record(msg, wParam, lParam));
        return created ? IntPtr.Zero : DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public List<Record> Snapshot()
    {
        lock (records) return new List<Record>(records);
    }

    public List<Record> WaitFor(int count, int timeoutMs = 3000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            lock (records)
            {
                if (records.Count >= count) return new List<Record>(records);
            }
            Thread.Sleep(10);
        }
        return Snapshot();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposedFlag, 1) != 0) return;
        if (handle != IntPtr.Zero) PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        thread.Join(3000);
        ready.Dispose();
    }

    public (int Left, int Top, int Right, int Bottom) GetRect()
    {
        GetWindowRect(handle, out RECT r);
        return (r.Left, r.Top, r.Right, r.Bottom);
    }

    delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? moduleName);
}
