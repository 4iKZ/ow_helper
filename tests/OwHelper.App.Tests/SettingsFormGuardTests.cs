using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using OwHelper.Desktop;
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

    [Fact]
    public void RenderForms_ToArtifactBitmap()
    {
        var thread = new Thread(() =>
        {
            var res = AppStartup.Initialize("Snapshot", _ => { }, _ => false);
            var controller = new TrayController(res.Session, res.Log, res.Config, res.Problems);

            using var mainForm = new MainForm(controller);
            mainForm.Show();
            mainForm.RefreshStatus();
            string dir = @"C:\Users\30253\.gemini\antigravity\brain\7a863414-1063-4849-881c-d02baa44569a";
            if (System.IO.Directory.Exists(dir))
            {
                using var bmp = new System.Drawing.Bitmap(mainForm.Width, mainForm.Height);
                mainForm.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, mainForm.Width, mainForm.Height));
                bmp.Save(System.IO.Path.Combine(dir, "main_form_rendered.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            using var settingsForm = new SettingsForm(controller);
            settingsForm.Show();
            if (System.IO.Directory.Exists(dir))
            {
                using var bmpSettings = new System.Drawing.Bitmap(settingsForm.Width, settingsForm.Height);
                settingsForm.DrawToBitmap(bmpSettings, new System.Drawing.Rectangle(0, 0, settingsForm.Width, settingsForm.Height));
                bmpSettings.Save(System.IO.Path.Combine(dir, "settings_form_rendered.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            // 渲染运行中状态
            _ = controller.Session.StartAsync();
            Thread.Sleep(300);
            mainForm.RefreshStatus();
            if (System.IO.Directory.Exists(dir))
            {
                using var bmpRunning = new System.Drawing.Bitmap(mainForm.Width, mainForm.Height);
                mainForm.DrawToBitmap(bmpRunning, new System.Drawing.Rectangle(0, 0, mainForm.Width, mainForm.Height));
                bmpRunning.Save(System.IO.Path.Combine(dir, "main_form_running.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            _ = controller.Session.StopAsync();

            mainForm.CloseForExit();
            settingsForm.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(15000);
    }
}

