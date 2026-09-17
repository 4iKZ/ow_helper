using System;
using System.Diagnostics;
using OwHelper.Core;

namespace OwHelper;

public static class RuntimeRecovery
{
    public static bool TryRecover(RuntimeStateStore store, Action<string> output, Func<string, bool> confirm)
    {
        RuntimeState? state = store.Load();
        if (state == null) return false;

        Process? process = null;
        try { process = Process.GetProcessById(state.Pid); }
        catch { }

        TargetValidationResult validation = TargetValidation.Validate(
            (IntPtr)state.Hwnd,
            state.Pid,
            process,
            state.ProcessStartTimeUtc);

        if (!validation.Valid)
        {
            store.Clear();
            output($"  上次运行的窗口状态已失效（{validation.Reason}），已清理");
            process?.Dispose();
            return false;
        }

        if (!confirm("发现上次异常退出时移出屏幕的窗口，按 Y 恢复，其他键跳过"))
        {
            output("  已跳过恢复（runtime-state.json 保留）");
            process?.Dispose();
            return false;
        }

        WindowPlacementResult result = WindowMover.MoveTo((IntPtr)state.Hwnd, state.Pid, state.Left, state.Top);
        output(result.Success ? "  上次移出屏幕的窗口已恢复" : $"  恢复失败: {result.Message}");
        if (result.Success) store.Clear();
        process?.Dispose();
        return result.Success;
    }
}

