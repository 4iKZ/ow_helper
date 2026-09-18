using OwHelper.Core;
using OwHelper.TestSupport;
using Xunit;

namespace OwHelper.App.Tests;

[CollectionDefinition("standin")]
public class StandInCollection
{
}

[Collection("standin")]
public class TargetSelectionTests
{
    [Fact]
    public void Find_WithWindowlessAndWindowedSameNameProcess_PicksWindowed()
    {
        using var empty = StandInProcess.Start("nowindow");
        using var windowed = StandInProcess.Start();

        GameWindow? found = GameWindow.Find(StandInProcess.Name);

        Assert.NotNull(found);
        Assert.NotEqual(empty.Process.Id, (int)found.Pid);
        Assert.Equal(640, found.Width);
        Assert.Equal(480, found.Height);
    }
}
