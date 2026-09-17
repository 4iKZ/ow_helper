using System.Linq;
using Xunit;

namespace OwHelper.Core.Tests;

public class PulseRunnerTests
{
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP = 0x0101;
    const uint WM_ACTIVATE = 0x0006;
    const uint WM_SETFOCUS = 0x0007;
    const uint WM_KILLFOCUS = 0x0008;
    const uint WM_ACTIVATEAPP = 0x001C;

    [Fact]
    public void DefaultRecipe_SendsFocusKeyDownKeyUpKillFocusInOrder()
    {
        using var window = new FakeWindow();
        var result = PulseRunner.Execute(window.Handle, new PulseRecipe { Keys = new[] { 0x10 }, HoldMs = 20 });

        var records = window.WaitFor(4);
        Assert.True(result.AllSucceeded);
        Assert.Equal(4, records.Count);
        Assert.Equal(WM_SETFOCUS, records[0].Msg);
        Assert.Equal(WM_KEYDOWN, records[1].Msg);
        Assert.Equal(0x10, (int)records[1].WParam);
        Assert.Equal(WM_KEYUP, records[2].Msg);
        Assert.Equal(WM_KILLFOCUS, records[3].Msg);
    }

    [Fact]
    public void MultipleKeys_DownInOrder_UpInReverse()
    {
        using var window = new FakeWindow();
        PulseRunner.Execute(window.Handle, new PulseRecipe { Keys = new[] { 0x10, 0x57 }, HoldMs = 20 });

        var records = window.WaitFor(6);
        Assert.Equal(
            new[] { WM_SETFOCUS, WM_KEYDOWN, WM_KEYDOWN, WM_KEYUP, WM_KEYUP, WM_KILLFOCUS },
            records.Select(r => r.Msg));
        Assert.Equal(0x10, (int)records[1].WParam);
        Assert.Equal(0x57, (int)records[2].WParam);
        Assert.Equal(0x57, (int)records[3].WParam);
        Assert.Equal(0x10, (int)records[4].WParam);
    }

    [Fact]
    public void KeyDownAndKeyUp_CarryScanCodeAndTransitionBits()
    {
        using var window = new FakeWindow();
        PulseRunner.Execute(window.Handle, new PulseRecipe { Keys = new[] { 0x10 }, HoldMs = 20 });

        var records = window.WaitFor(4);
        long down = records[1].LParam.ToInt64();
        long up = records[2].LParam.ToInt64();
        Assert.Equal(0x2A, (down >> 16) & 0xFF);
        Assert.Equal(0, (down >> 30) & 0x3);
        Assert.Equal(0x2A, (up >> 16) & 0xFF);
        Assert.Equal(1, (up >> 30) & 0x1);
        Assert.Equal(1, (up >> 31) & 0x1);
    }

    [Fact]
    public void FullActivationRecipe_SendsActivateAppAndActivateAroundPulse()
    {
        using var window = new FakeWindow();
        PulseRunner.Execute(window.Handle, new PulseRecipe
        {
            Keys = new[] { 0x10 },
            SendActivateApp = true,
            SendActivate = true,
            HoldMs = 20,
        });

        var records = window.WaitFor(8);
        Assert.Equal(
            new[] { WM_ACTIVATEAPP, WM_ACTIVATE, WM_SETFOCUS, WM_KEYDOWN, WM_KEYUP, WM_KILLFOCUS, WM_ACTIVATE, WM_ACTIVATEAPP },
            records.Select(r => r.Msg));
        Assert.Equal(1, (int)records[0].WParam);
        Assert.Equal(1, (int)records[1].WParam);
        Assert.Equal(0, (int)records[6].WParam);
        Assert.Equal(0, (int)records[7].WParam);
    }

    [Fact]
    public void NoFocusRecipe_SendsOnlyKeyMessages()
    {
        using var window = new FakeWindow();
        PulseRunner.Execute(window.Handle, new PulseRecipe { Keys = new[] { 0x10 }, SendFocus = false, FocusWaitMs = 0, HoldMs = 20 });

        var records = window.WaitFor(2);
        Assert.Equal(2, records.Count);
        Assert.Equal(WM_KEYDOWN, records[0].Msg);
        Assert.Equal(WM_KEYUP, records[1].Msg);
    }

    [Fact]
    public void SendKey_Repeat_SetsPreviousStateBitOnly()
    {
        using var window = new FakeWindow();
        PulseRunner.SendKey(window.Handle, 0x10, down: true, repeat: true);

        var records = window.WaitFor(1);
        long lp = records[0].LParam.ToInt64();
        Assert.Equal(WM_KEYDOWN, records[0].Msg);
        Assert.Equal(1, (lp >> 30) & 0x1);
        Assert.Equal(0, (lp >> 31) & 0x1);
    }

    [Fact]
    public void Result_ReportsPerMessageOutcomes()
    {
        using var window = new FakeWindow();
        var result = PulseRunner.Execute(window.Handle, new PulseRecipe { Keys = new[] { 0x10, 0x57 }, HoldMs = 10 });

        Assert.True(result.AllSucceeded);
        Assert.Equal(6, result.Messages.Count);
        Assert.Equal("SETFOCUS", result.Messages[0].Name);
        Assert.Equal("KEYDOWN(0x10)", result.Messages[1].Name);
        Assert.Equal("KEYDOWN(0x57)", result.Messages[2].Name);
        Assert.Equal("KEYUP(0x57)", result.Messages[3].Name);
        Assert.Equal("KEYUP(0x10)", result.Messages[4].Name);
        Assert.Equal("KILLFOCUS", result.Messages[5].Name);
    }
}
