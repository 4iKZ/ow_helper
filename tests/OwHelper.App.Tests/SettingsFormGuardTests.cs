using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using OwHelper.Core;
using OwHelper.Desktop;
using OwHelper.TestSupport;
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

            mainForm.CloseForExit();
            settingsForm.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(15000);
    }

    [Fact]
    public void RoundedControls_DoNotRenderBlackBordersOrCorners()
    {
        var thread = new Thread(() =>
        {
            // 1. 测试 RoundedButton
            using var panel = new Panel { Size = new System.Drawing.Size(200, 100), BackColor = Palette.Paper };
            using var btn = new RoundedButton
            {
                Size = new System.Drawing.Size(120, 36),
                CornerRadius = 8,
                BorderSize = 0,
                BackColor = Palette.Accent,
                ForeColor = System.Drawing.Color.White,
                Text = "停止挂机",
            };
            panel.Controls.Add(btn);

            using var bmpBtn = new System.Drawing.Bitmap(btn.Width, btn.Height);
            btn.DrawToBitmap(bmpBtn, new System.Drawing.Rectangle(0, 0, btn.Width, btn.Height));

            // 四个顶点必须是父容器颜色 Palette.Paper，绝不能是黑色 (0, 0, 0)
            var cTopLeft = bmpBtn.GetPixel(0, 0);
            var cTopRight = bmpBtn.GetPixel(btn.Width - 1, 0);
            var cBottomLeft = bmpBtn.GetPixel(0, btn.Height - 1);
            var cBottomRight = bmpBtn.GetPixel(btn.Width - 1, btn.Height - 1);

            Assert.Equal(Palette.Paper.R, cTopLeft.R);
            Assert.Equal(Palette.Paper.G, cTopLeft.G);
            Assert.Equal(Palette.Paper.B, cTopLeft.B);

            Assert.Equal(Palette.Paper.R, cTopRight.R);
            Assert.Equal(Palette.Paper.G, cTopRight.G);
            Assert.Equal(Palette.Paper.B, cTopRight.B);

            Assert.Equal(Palette.Paper.R, cBottomLeft.R);
            Assert.Equal(Palette.Paper.G, cBottomLeft.G);
            Assert.Equal(Palette.Paper.B, cBottomLeft.B);

            Assert.Equal(Palette.Paper.R, cBottomRight.R);
            Assert.Equal(Palette.Paper.G, cBottomRight.G);
            Assert.Equal(Palette.Paper.B, cBottomRight.B);

            // 整个底边与边缘绝不能存在未初始化的纯黑像素 (0, 0, 0)
            for (int x = 0; x < btn.Width; x++)
            {
                var c = bmpBtn.GetPixel(x, btn.Height - 1);
                Assert.False(c.R == 0 && c.G == 0 && c.B == 0, $"底边像素 ({x}, {btn.Height - 1}) 为纯黑！");
            }

            // 2. 测试 MetricTile
            using var tilePanel = new Panel { Size = new System.Drawing.Size(200, 100), BackColor = Palette.Panel };
            using var tile = new MetricTile
            {
                Size = new System.Drawing.Size(120, 56),
                Title = "运行状态",
                ValueText = "正在挂机",
            };
            tilePanel.Controls.Add(tile);

            using var bmpTile = new System.Drawing.Bitmap(tile.Width, tile.Height);
            tile.DrawToBitmap(bmpTile, new System.Drawing.Rectangle(0, 0, tile.Width, tile.Height));

            var cTileTL = bmpTile.GetPixel(0, 0);
            var cTileBR = bmpTile.GetPixel(tile.Width - 1, tile.Height - 1);
            Assert.Equal(Palette.Panel.R, cTileTL.R);
            Assert.Equal(Palette.Panel.G, cTileTL.G);
            Assert.Equal(Palette.Panel.B, cTileTL.B);
            Assert.Equal(Palette.Panel.R, cTileBR.R);
            Assert.Equal(Palette.Panel.G, cTileBR.G);
            Assert.Equal(Palette.Panel.B, cTileBR.B);

            // 3. 测试 PulseProgressBar
            using var barPanel = new Panel { Size = new System.Drawing.Size(200, 100), BackColor = Palette.Panel };
            using var bar = new PulseProgressBar
            {
                Size = new System.Drawing.Size(120, 10),
                Progress = 0.5,
            };
            barPanel.Controls.Add(bar);

            using var bmpBar = new System.Drawing.Bitmap(bar.Width, bar.Height);
            bar.DrawToBitmap(bmpBar, new System.Drawing.Rectangle(0, 0, bar.Width, bar.Height));

            var cBarTL = bmpBar.GetPixel(0, 0);
            var cBarBR = bmpBar.GetPixel(bar.Width - 1, bar.Height - 1);
            Assert.Equal(Palette.Panel.R, cBarTL.R);
            Assert.Equal(Palette.Panel.G, cBarTL.G);
            Assert.Equal(Palette.Panel.B, cBarTL.B);
            Assert.Equal(Palette.Panel.R, cBarBR.R);
            Assert.Equal(Palette.Panel.G, cBarBR.G);
            Assert.Equal(Palette.Panel.B, cBarBR.B);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(15000);
    }
}

