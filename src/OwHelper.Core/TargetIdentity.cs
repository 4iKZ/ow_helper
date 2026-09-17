using System;
using System.Diagnostics;

namespace OwHelper.Core;

public sealed record TargetIdentity(
    int Pid,
    nint Hwnd,
    string ProcessName,
    string WindowClass,
    string WindowTitle,
    DateTime? ProcessStartTimeUtc);

public sealed record TargetValidationResult(bool Valid, string Reason)
{
    public static TargetValidationResult Ok { get; } = new TargetValidationResult(true, "alive");
}

public static class TargetValidation
{
    public static TargetValidationResult Validate(IntPtr hwnd, int pid, Process? process, DateTime? processStartTimeUtc)
    {
        if (process != null)
        {
            try
            {
                if (process.HasExited) return new TargetValidationResult(false, "process exited");
            }
            catch (Exception ex)
            {
                return new TargetValidationResult(false, $"process state unavailable: {ex.Message}");
            }
        }

        if (!Native.IsWindow(hwnd))
        {
            return new TargetValidationResult(false, "window handle is gone");
        }

        Native.GetWindowThreadProcessId(hwnd, out uint owner);
        if (owner != (uint)pid)
        {
            return new TargetValidationResult(false, $"hwnd now belongs to pid {owner} (expected {pid})");
        }

        if (processStartTimeUtc is DateTime expectedStart)
        {
            if (process == null)
            {
                return new TargetValidationResult(false, "process object missing for start-time check");
            }
            try
            {
                if (process.StartTime.ToUniversalTime() != expectedStart)
                {
                    return new TargetValidationResult(false, "process restarted (start time mismatch)");
                }
            }
            catch (Exception ex)
            {
                return new TargetValidationResult(false, $"start time unavailable: {ex.Message}");
            }
        }

        return TargetValidationResult.Ok;
    }
}
