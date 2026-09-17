using System;
using OwHelper.Core;
using OwHelper.TestSupport;
using Xunit;

namespace OwHelper.Core.Tests;

public class MouseInputTests
{
    const uint WM_MOUSEMOVE = 0x0200;
    const uint WM_LBUTTONDOWN = 0x0201;
    const uint WM_LBUTTONUP = 0x0202;
    const uint WM_RBUTTONDOWN = 0x0204;
    const uint WM_MBUTTONDOWN = 0x0207;
    const uint WM_XBUTTONDOWN = 0x020B;

    [Theory]
    [InlineData(0x01, true)]
    [InlineData(0x02, true)]
    [InlineData(0x04, true)]
    [InlineData(0x05, true)]
    [InlineData(0x06, true)]
    [InlineData(0x10, false)]
    [InlineData(0x26, false)]
    public void IsMouseVirtualKey_Classifies(int vk, bool expected)
    {
        Assert.Equal(expected, MouseInput.IsMouseVirtualKey(vk));
    }

    [Fact]
    public void TryGetClientCenter_ReturnsCenterOfClientArea()
    {
        using var window = new FakeWindow();
        var (width, height) = WindowProbe.GetClientSize(window.Handle);

        Assert.True(MouseInput.TryGetClientCenter(window.Handle, out int x, out int y));
        Assert.Equal(width / 2, x);
        Assert.Equal(height / 2, y);
    }

    [Fact]
    public void SendMove_PostsMouseMoveWithPackedCoordinates()
    {
        using var window = new FakeWindow();

        Assert.True(MouseInput.SendMove(window.Handle, 50, 50));

        var record = window.WaitFor(1)[0];
        long lParam = record.LParam.ToInt64();
        Assert.Equal(WM_MOUSEMOVE, record.Msg);
        Assert.Equal(50, (int)(lParam & 0xFFFF));
        Assert.Equal(50, (int)((lParam >> 16) & 0xFFFF));
    }

    [Fact]
    public void SendButton_LeftButton_CarriesButtonFlagAndPoint()
    {
        using var window = new FakeWindow();

        Assert.True(MouseInput.SendButton(window.Handle, MouseInput.VkLeftButton, down: true, x: 10, y: 20));
        Assert.True(MouseInput.SendButton(window.Handle, MouseInput.VkLeftButton, down: false, x: 10, y: 20));

        var records = window.WaitFor(2);
        long lParam = records[0].LParam.ToInt64();
        Assert.Equal(WM_LBUTTONDOWN, records[0].Msg);
        Assert.Equal(0x0001, (int)records[0].WParam);
        Assert.Equal(WM_LBUTTONUP, records[1].Msg);
        Assert.Equal(0x0001, (int)records[1].WParam);
        Assert.Equal(10, (int)(lParam & 0xFFFF));
        Assert.Equal(20, (int)((lParam >> 16) & 0xFFFF));
    }

    [Fact]
    public void SendButton_RightAndMiddle_MapToTheirMessages()
    {
        using var window = new FakeWindow();

        MouseInput.SendButton(window.Handle, MouseInput.VkRightButton, down: true, x: 0, y: 0);
        MouseInput.SendButton(window.Handle, MouseInput.VkMiddleButton, down: true, x: 0, y: 0);

        var records = window.WaitFor(2);
        Assert.Equal(WM_RBUTTONDOWN, records[0].Msg);
        Assert.Equal(0x0002, (int)records[0].WParam);
        Assert.Equal(WM_MBUTTONDOWN, records[1].Msg);
        Assert.Equal(0x0010, (int)records[1].WParam);
    }

    [Theory]
    [InlineData(0x05, 1)]
    [InlineData(0x06, 2)]
    public void SendButton_XButtons_PutIndexInHighWord(int vk, int expectedHighWord)
    {
        using var window = new FakeWindow();

        MouseInput.SendButton(window.Handle, vk, down: true, x: 0, y: 0);

        var record = window.WaitFor(1)[0];
        Assert.Equal(WM_XBUTTONDOWN, record.Msg);
        Assert.Equal(expectedHighWord, (int)(record.WParam.ToInt64() >> 16));
    }

    [Fact]
    public void SendButton_NonMouseKey_IsRejected()
    {
        using var window = new FakeWindow();

        Assert.False(MouseInput.SendButton(window.Handle, 0x10, down: true, x: 0, y: 0));
    }
}
