using System;
using Xunit;

namespace OwHelper.Core.Tests;

public class KeyNamesTests
{
    [Theory]
    [InlineData("shift", 0x10)]
    [InlineData("SHIFT", 0x10)]
    [InlineData("ctrl", 0x11)]
    [InlineData("control", 0x11)]
    [InlineData("alt", 0x12)]
    [InlineData("space", 0x20)]
    [InlineData("enter", 0x0D)]
    [InlineData("return", 0x0D)]
    [InlineData("tab", 0x09)]
    [InlineData("esc", 0x1B)]
    [InlineData(" escape ", 0x1B)]
    [InlineData("w", 0x57)]
    [InlineData("a", 0x41)]
    [InlineData("5", 0x35)]
    [InlineData("f1", 0x70)]
    [InlineData("f12", 0x7B)]
    [InlineData("up", 0x26)]
    [InlineData("down", 0x28)]
    [InlineData("left", 0x25)]
    [InlineData("right", 0x27)]
    [InlineData("lshift", 0xA0)]
    [InlineData("rshift", 0xA1)]
    [InlineData("lctrl", 0xA2)]
    [InlineData("rctrl", 0xA3)]
    [InlineData("lalt", 0xA4)]
    [InlineData("ralt", 0xA5)]
    [InlineData("insert", 0x2D)]
    [InlineData("delete", 0x2E)]
    [InlineData("del", 0x2E)]
    [InlineData("home", 0x24)]
    [InlineData("end", 0x23)]
    [InlineData("pageup", 0x21)]
    [InlineData("pagedown", 0x22)]
    [InlineData("printscreen", 0x2C)]
    [InlineData("numlock", 0x90)]
    [InlineData("numdivide", 0x6F)]
    public void Parse_ReturnsVirtualKey(string name, int expected)
    {
        Assert.Equal(expected, KeyNames.Parse(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("f13")]
    [InlineData("f0")]
    [InlineData("!!")]
    public void Parse_RejectsUnknownNames(string name)
    {
        Assert.Throws<ArgumentException>(() => KeyNames.Parse(name));
    }
}
