using System.Linq;
using OwHelper.Core;
using OwHelper.TestSupport;
using Xunit;

namespace OwHelper.Core.Tests;

public class PulseRunnerMouseTests
{
    const uint WM_MOUSEMOVE = 0x0200;
    const uint WM_LBUTTONDOWN = 0x0201;
    const uint WM_LBUTTONUP = 0x0202;
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP = 0x0101;
    const uint WM_SETFOCUS = 0x0007;
    const uint WM_KILLFOCUS = 0x0008;

    [Fact]
    public void MouseOnlyRecipe_SendsMoveThenButton_WithCenterCoordinates()
    {
        using var window = new FakeWindow();
        var (width, height) = WindowProbe.GetClientSize(window.Handle);

        var result = PulseRunner.Execute(window.Handle, new PulseRecipe
        {
            Keys = new[] { MouseInput.VkLeftButton },
            HoldMs = 20,
        });

        Assert.True(result.AllSucceeded);
        Assert.Equal(
            new[] { "SETFOCUS", "MOUSEMOVE", "MOUSEDOWN(0x01)", "MOUSEUP(0x01)", "KILLFOCUS" },
            result.Messages.Select(m => m.Name));

        var records = window.WaitFor(5);
        Assert.Equal(WM_SETFOCUS, records[0].Msg);
        Assert.Equal(WM_MOUSEMOVE, records[1].Msg);
        Assert.Equal(WM_LBUTTONDOWN, records[2].Msg);
        Assert.Equal(WM_LBUTTONUP, records[3].Msg);
        Assert.Equal(WM_KILLFOCUS, records[4].Msg);

        long lParam = records[2].LParam.ToInt64();
        Assert.Equal(width / 2, (int)(lParam & 0xFFFF));
        Assert.Equal(height / 2, (int)((lParam >> 16) & 0xFFFF));
        Assert.Equal(0x0001, (int)records[2].WParam);
    }

    [Fact]
    public void MixedKeyboardAndMouse_DownInOrder_UpInReverse()
    {
        using var window = new FakeWindow();

        var result = PulseRunner.Execute(window.Handle, new PulseRecipe
        {
            Keys = new[] { 0x10, MouseInput.VkLeftButton },
            HoldMs = 20,
        });

        Assert.Equal(
            new[] { "SETFOCUS", "MOUSEMOVE", "KEYDOWN(0x10)", "MOUSEDOWN(0x01)", "MOUSEUP(0x01)", "KEYUP(0x10)", "KILLFOCUS" },
            result.Messages.Select(m => m.Name));

        var records = window.WaitFor(7);
        Assert.Equal(WM_SETFOCUS, records[0].Msg);
        Assert.Equal(WM_MOUSEMOVE, records[1].Msg);
        Assert.Equal(WM_KEYDOWN, records[2].Msg);
        Assert.Equal(WM_LBUTTONDOWN, records[3].Msg);
        Assert.Equal(WM_LBUTTONUP, records[4].Msg);
        Assert.Equal(WM_KEYUP, records[5].Msg);
        Assert.Equal(WM_KILLFOCUS, records[6].Msg);
    }

    [Fact]
    public void NoFocusMouseRecipe_SendsMoveAndButtonOnly()
    {
        using var window = new FakeWindow();

        var result = PulseRunner.Execute(window.Handle, new PulseRecipe
        {
            Keys = new[] { MouseInput.VkRightButton },
            SendFocus = false,
            FocusWaitMs = 0,
            HoldMs = 20,
        });

        Assert.Equal(
            new[] { "MOUSEMOVE", "MOUSEDOWN(0x02)", "MOUSEUP(0x02)" },
            result.Messages.Select(m => m.Name));
    }

    [Fact]
    public void KeyboardOnlyRecipe_DoesNotSendMouseMove()
    {
        using var window = new FakeWindow();

        var result = PulseRunner.Execute(window.Handle, new PulseRecipe
        {
            Keys = new[] { 0x10 },
            HoldMs = 20,
        });

        Assert.DoesNotContain(result.Messages, m => m.Name == "MOUSEMOVE");
    }

    [Fact]
    public void Execute_LeftRightChord_ReflectsAggregateButtonState()
    {
        using var window = new FakeWindow();
        const uint WM_RBUTTONDOWN = 0x0204;
        const uint WM_RBUTTONUP = 0x0205;

        PulseRunner.Execute(window.Handle, new PulseRecipe
        {
            Keys = new[] { MouseInput.VkLeftButton, MouseInput.VkRightButton },
            SendFocus = false,
            FocusWaitMs = 0,
            HoldMs = 20,
        });

        var records = window.WaitFor(5);
        Assert.Equal(WM_MOUSEMOVE, records[0].Msg);
        Assert.Equal(WM_LBUTTONDOWN, records[1].Msg);
        Assert.Equal(WM_RBUTTONDOWN, records[2].Msg);
        Assert.Equal(WM_RBUTTONUP, records[3].Msg);
        Assert.Equal(WM_LBUTTONUP, records[4].Msg);
        Assert.Equal(0x0000, (int)records[0].WParam);
        Assert.Equal(0x0001, (int)records[1].WParam);
        Assert.Equal(0x0003, (int)records[2].WParam);
        Assert.Equal(0x0001, (int)records[3].WParam);
        Assert.Equal(0x0000, (int)records[4].WParam);
    }

    [Fact]
    public void Execute_ShiftPlusLeft_CarriesShiftModifier()
    {
        using var window = new FakeWindow();
        const int MK_SHIFT = 0x0004;
        const int MK_LBUTTON = 0x0001;

        PulseRunner.Execute(window.Handle, new PulseRecipe
        {
            Keys = new[] { 0x10, MouseInput.VkLeftButton },
            SendFocus = false,
            FocusWaitMs = 0,
            HoldMs = 20,
        });

        var records = window.WaitFor(5);
        Assert.Equal(WM_KEYDOWN, records[1].Msg);
        Assert.Equal(WM_LBUTTONDOWN, records[2].Msg);
        Assert.Equal(WM_LBUTTONUP, records[3].Msg);
        Assert.Equal(WM_KEYUP, records[4].Msg);
        Assert.Equal(MK_SHIFT | MK_LBUTTON, (int)records[2].WParam);
        Assert.Equal(MK_SHIFT, (int)records[3].WParam);
    }
}
