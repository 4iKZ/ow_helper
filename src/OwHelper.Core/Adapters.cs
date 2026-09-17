using System;

namespace OwHelper.Core;

public sealed class GameWindowLocator : ITargetLocator
{
    public GameWindow? Find(string processName) => GameWindow.Find(processName);
}

public sealed class PulseSender : IPulseSender
{
    public PulseResult Execute(IntPtr hwnd, PulseRecipe recipe) => PulseRunner.Execute(hwnd, recipe);
}

public sealed class WindowPlacementController : IWindowPlacementController
{
    WindowPlacement? current;

    public bool IsOffscreen => current?.IsOffscreen ?? false;

    public bool TaskbarHidden => current?.TaskbarHidden ?? false;

    public long OriginalExStyle => current?.OriginalExStyle ?? 0;

    public WindowPlacementResult MoveOffscreen(IntPtr hwnd, int pid, bool hideFromTaskbar)
    {
        if (current == null || current.Handle != hwnd || current.Pid != pid)
        {
            current = new WindowPlacement(hwnd, pid);
        }
        return current.MoveOffscreen(hideFromTaskbar);
    }

    public WindowPlacementResult Restore()
        => current == null
            ? new WindowPlacementResult(true, "nothing to restore", null)
            : current.Restore();

    public bool TryGetOriginalPosition(out int left, out int top)
    {
        if (current != null) return current.TryGetSavedPosition(out left, out top);
        left = 0;
        top = 0;
        return false;
    }
}
