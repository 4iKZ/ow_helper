using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using OwHelper.Tray;
using Xunit;

namespace OwHelper.App.Tests;

public class SettingsFormGuardTests
{
    [Fact]
    public void ApplySelection_CheckingItemsDuringPopulation_DoesNotThrow()
    {
        Exception? captured = null;
        int checkedCount = -1;
        List<string> checkedNames = new List<string>();

        var thread = new Thread(() =>
        {
            try
            {
                using var list = new CheckedListBox();
                SettingsForm.Populate(list);
                SettingsForm.ApplySelection(list, new[] { "shift", "mouseleft", "f1" });

                checkedCount = list.CheckedItems.Count;
                checkedNames = list.CheckedItems.Cast<SettingsForm.KeyItem>().Select(k => k.Name).ToList();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(15000);

        Assert.Null(captured);
        Assert.Equal(2, checkedCount);
        Assert.Contains("shift", checkedNames);
        Assert.Contains("mouseleft", checkedNames);
    }

    [Fact]
    public void ApplySelection_EmptySelection_UnchecksEverything()
    {
        Exception? captured = null;
        int checkedCount = -1;

        var thread = new Thread(() =>
        {
            try
            {
                using var list = new CheckedListBox();
                SettingsForm.Populate(list);
                SettingsForm.ApplySelection(list, new[] { "shift" });
                SettingsForm.ApplySelection(list, Array.Empty<string>());
                checkedCount = list.CheckedItems.Count;
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(15000);

        Assert.Null(captured);
        Assert.Equal(0, checkedCount);
    }
}
