using System;
using System.Diagnostics;

namespace OwHelper.Core;

public interface ITargetLocator
{
    GameWindow? Find(string processName);
}

public interface IPulseSender
{
    PulseResult Execute(IntPtr hwnd, PulseRecipe recipe);
}

public interface IResourceGovernor
{
    ResourceSnapshot? Snapshot { get; }
    ResourceApplyResult Apply();
    ResourceRestoreResult Restore();
}

public interface IWindowPlacementController
{
    bool IsOffscreen { get; }
    WindowPlacementResult MoveOffscreen(IntPtr hwnd, int pid);
    WindowPlacementResult Restore();
}
