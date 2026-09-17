using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace OwHelper.Core.Tests;

sealed class FakeWindow : IDisposable
{
    const int HWND_MESSAGE = -3;
    const uint WM_CLOSE = 0x0010;
    const uint WM_DESTROY = 0x0002;

    public readonly record struct Record(uint Msg, IntPtr WParam, IntPtr LParam);

    readonly Thread thread;
    readonly List<Record> records = new();
    readonly ManualResetEventSlim ready = new(false);
    readonly string className = "FakeWindow_" + Guid.NewGuid().ToString("N");
    readonly WndProcDelegate wndProc;
    volatile bool created;
    IntPtr handle = IntPtr.Zero;

    public IntPtr Handle => handle;

    public FakeWindow()
    {
        wndProc = WndProc;
        thread = new Thread(ThreadMain) { IsBackground = true };
        thread.Start();
        ready.Wait(5000);
        lock (records) records.Clear();
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
        handle = CreateWindowEx(0, className, className, 0, 0, 0, 100, 100, new IntPtr(HWND_MESSAGE), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        created = true;
        ready.Set();
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
        if (handle != IntPtr.Zero) PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        thread.Join(3000);
        ready.Dispose();
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
