using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using DailyBingWallpaper;
using Microsoft.Win32;

internal static class Setup
{
    internal const string AppName = "Daily Bing Wallpaper";
    private const string ProcessName = "DailyBingWallpaper";
    private const string PayloadName = ProcessName + ".exe";
    private const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DailyBingWallpaper";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = ProcessName;
    private const string TaskName = ProcessName;

    private static bool silent;

    [STAThread]
    private static int Main(string[] args)
    {
        silent = Array.IndexOf(args, "/S") >= 0;

        if (Array.IndexOf(args, "--uninstall") >= 0)
        {
            return Uninstall();
        }

        if (!silent)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
            return 0;
        }

        try
        {
            PerformInstall();
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    internal static string InstallFolder
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "DailyBingWallpaper");
        }
    }

    internal static string PerformInstall()
    {
        // A running copy holds its exe open, which blocks the overwrite during a reinstall.
        StopRunningApp();

        string folder = InstallFolder;
        Directory.CreateDirectory(folder);

        string appPath = Path.Combine(folder, PayloadName);
        using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadName))
        {
            if (payload == null)
            {
                throw new InvalidOperationException("The installer payload is missing.");
            }

            using (FileStream file = File.Create(appPath))
            {
                payload.CopyTo(file);
            }
        }

        string uninstallerPath = Path.Combine(folder, "Uninstall.exe");
        File.Copy(Shortcuts.ExecutablePath, uninstallerPath, true);

        Shortcuts.Create(Shortcuts.StartMenuPath, appPath);
        Shortcuts.Create(Shortcuts.DesktopPath, appPath);
        WriteUninstallEntry(appPath, uninstallerPath, folder);

        return appPath;
    }

    private static void WriteUninstallEntry(string appPath, string uninstallerPath, string folder)
    {
        int kilobytes = (int)((new FileInfo(appPath).Length + new FileInfo(uninstallerPath).Length) / 1024);

        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
        {
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", BuildInfo.Date);
            key.SetValue("DisplayIcon", appPath);
            key.SetValue("InstallLocation", folder);
            key.SetValue("UninstallString", "\"" + uninstallerPath + "\" --uninstall");
            key.SetValue("QuietUninstallString", "\"" + uninstallerPath + "\" --uninstall /S");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", kilobytes, RegistryValueKind.DWord);
        }
    }

    private static int Uninstall()
    {
        string folder = InstallFolder;

        if (!Confirm(
            "Remove " + AppName + " from this user account?\n\n" +
            "Its desktop and Start menu shortcuts, sign-in entry and daily task are removed.\n" +
            "The last wallpaper image stays on disk.",
            MessageBoxIcon.Warning))
        {
            return 2;
        }

        try
        {
            StopRunningApp();
            Shortcuts.Remove(Shortcuts.StartMenuPath);
            Shortcuts.Remove(Shortcuts.DesktopPath);
            RemoveOurRunValue(folder);
            RemoveOurScheduledTask(folder);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);
            RemoveFolderAfterExit(folder);

            Inform("Removed " + AppName + ".", MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            Inform("Uninstall failed:\n\n" + ex.Message, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void StopRunningApp()
    {
        foreach (Process running in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                running.Kill();
                // Windows keeps the exe locked until the process has fully exited.
                running.WaitForExit(5000);
            }
            catch
            {
                // Already exiting.
            }
        }
    }

    private static void RemoveOurRunValue(string folder)
    {
        using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
        {
            if (run == null)
            {
                return;
            }

            object value = run.GetValue(RunValueName);
            if (value != null && value.ToString().IndexOf(folder, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                run.DeleteValue(RunValueName, false);
            }
        }
    }

    // Only removes the task when it points at this installation, not at a portable copy.
    private static void RemoveOurScheduledTask(string folder)
    {
        string xml = Run("schtasks.exe", "/Query /TN " + TaskName + " /XML");
        if (xml.IndexOf(folder, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        Run("schtasks.exe", "/Delete /TN " + TaskName + " /F");
    }

    // The uninstaller lives inside the folder it deletes, so hand the removal to cmd after exit.
    private static void RemoveFolderAfterExit(string folder)
    {
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = "cmd.exe";
        info.Arguments = "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + folder + "\"";
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.WindowStyle = ProcessWindowStyle.Hidden;
        Process.Start(info);
    }

    private static bool Confirm(string text, MessageBoxIcon icon)
    {
        return silent ||
            MessageBox.Show(text, AppName, MessageBoxButtons.YesNo, icon) == DialogResult.Yes;
    }

    private static void Inform(string text, MessageBoxIcon icon)
    {
        if (!silent)
        {
            MessageBox.Show(text, AppName, MessageBoxButtons.OK, icon);
        }
    }

    private static string Run(string fileName, string arguments)
    {
        try
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = fileName;
            info.Arguments = arguments;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            using (Process process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                return output;
            }
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

internal sealed class SetupForm : Form
{
    private readonly Label statusLabel;
    private readonly Button primaryButton;
    private string installedPath;

    internal SetupForm()
    {
        Text = Setup.AppName + " Setup";
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 232);
        AutoScaleMode = AutoScaleMode.Font;

        Label heading = new Label();
        heading.SetBounds(16, 14, 408, 26);
        heading.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
        heading.Text = Setup.AppName;

        Label description = new Label();
        description.SetBounds(16, 44, 408, 36);
        description.Text = "Sets your desktop background to Bing's daily photo. "
            + "No administrator rights are needed.";

        Label folderCaption = new Label();
        folderCaption.SetBounds(16, 88, 408, 18);
        folderCaption.ForeColor = SystemColors.GrayText;
        folderCaption.Text = "Installs to:";

        Label folderLabel = new Label();
        folderLabel.SetBounds(16, 106, 408, 18);
        folderLabel.AutoEllipsis = true;
        folderLabel.Text = Setup.InstallFolder;

        statusLabel = new Label();
        statusLabel.SetBounds(16, 136, 408, 34);
        statusLabel.ForeColor = SystemColors.GrayText;

        primaryButton = new Button();
        primaryButton.SetBounds(16, 182, 120, 32);
        primaryButton.Text = "Install";
        primaryButton.Click += OnPrimaryClicked;

        Button closeButton = new Button();
        closeButton.SetBounds(148, 182, 100, 32);
        closeButton.Text = "Close";
        closeButton.Click += delegate { Close(); };

        AcceptButton = primaryButton;

        Controls.Add(heading);
        Controls.Add(description);
        Controls.Add(folderCaption);
        Controls.Add(folderLabel);
        Controls.Add(statusLabel);
        Controls.Add(primaryButton);
        Controls.Add(closeButton);
    }

    private void OnPrimaryClicked(object sender, EventArgs e)
    {
        if (installedPath != null)
        {
            Process.Start(installedPath);
            Close();
            return;
        }

        primaryButton.Enabled = false;
        statusLabel.Text = "Installing...";

        try
        {
            installedPath = Setup.PerformInstall();
            statusLabel.Text = "Installed. Shortcuts are on your desktop and in the Start menu.";
            primaryButton.Text = "Open";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Installation failed: " + ex.Message;
        }
        finally
        {
            primaryButton.Enabled = true;
        }
    }
}
