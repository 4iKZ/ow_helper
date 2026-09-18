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
    ResourceApplyResult Apply(ResourcePolicy policy);
    ResourceRestoreResult Restore();
}

public interface IWindowPlacementController
{
    bool IsOffscreen { get; }
    bool StyleRestorePending { get; }
    bool NeedsRestore { get; }
    long OriginalExStyle { get; }
    WindowPlacementResult MoveOffscreen(IntPtr hwnd, int pid, bool hideFromTaskbar);
    WindowPlacementResult Restore(bool activate = false);
    bool TryGetOriginalPosition(out int left, out int top);
}
