using System;
using System.Windows.Forms;
using OwHelper;

namespace OwHelper.Desktop;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var single = new SingleInstance(AppStartup.SingleInstanceName);
        if (!single.Acquired)
        {
            MessageBox.Show("OwHelper 已在运行（托盘或控制台前端）。", "OW Helper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new TrayApplicationContext());
    }
}

