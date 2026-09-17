using System;

namespace OwHelper.Core;

public sealed class CursorState
{
    Native.POINT saved;

    public void Save() => Native.GetCursorPos(out saved);
    public void RestoreSaved() => Native.SetCursorPos(saved.X, saved.Y);
    public static void ReleaseClip() => Native.ClipCursor(IntPtr.Zero);
}
