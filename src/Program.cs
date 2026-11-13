using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DailyBingWallpaper
{
    internal static class Desktop
    {
        private const int SpiSetDeskWallpaper = 0x0014;
        private const int SpifUpdateIniFile = 0x01;
        private const int SpifSendChange = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SystemParametersInfo(int action, int param, string value, int flags);

        internal static void SetWallpaper(string path)
        {
            int result = SystemParametersInfo(
                SpiSetDeskWallpaper,
                0,
                path,
                SpifUpdateIniFile | SpifSendChange);

            if (result == 0)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    "Windows rejected the wallpaper change (error " + error + ").");
            }
        }
    }

    /// <summary>
    /// Optional daily refresh through Windows Task Scheduler, for machines left on for days.
    /// The task runs in the current user's context and needs no elevation.
    /// </summary>
    internal static class Scheduler
    {
        internal const string TaskName = "DailyBingWallpaper";
        private static string lastOutput = string.Empty;

        internal static bool IsEnabled()
        {
            return Run("/Query /TN \"" + TaskName + "\"") == 0;
        }

        internal static void Enable(string time)
        {
            string executable = "\"\\\"" + Application.ExecutablePath + "\\\" --silent\"";
            string arguments = "/Create /TN \"" + TaskName + "\" /TR " + executable
                + " /SC DAILY /ST " + time + " /F";

            int exit = Run(arguments);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    "Task Scheduler refused the daily task (schtasks exit " + exit + "). " + lastOutput.Trim());
            }
        }

        internal static void Disable()
        {
            if (!IsEnabled())
            {
                return;
            }

            int exit = Run("/Delete /TN \"" + TaskName + "\" /F");
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    "The daily task could not be removed (schtasks exit " + exit + "). " + lastOutput.Trim());
            }
        }

        private static int Run(string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "schtasks.exe";
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                lastOutput = output;
                return process.ExitCode;
            }
        }
    }

    internal static class Program
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "DailyBingWallpaper";
        private const int MaxLogLines = 40;
        internal const string SilentArgument = "--silent";

        [STAThread]
        private static int Main(string[] args)
        {
            Shortcuts.Create(Shortcuts.StartMenuPath, Shortcuts.ExecutablePath);

            if (Array.IndexOf(args, SilentArgument) >= 0)
            {
                return RunSilentAsync().GetAwaiter().GetResult();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static async Task<int> RunSilentAsync()
        {
            try
            {
                DailyImage image = await WallpaperService.FetchTodayAsync().ConfigureAwait(false);
                string path = await WallpaperService.DownloadAsync(image).ConfigureAwait(false);
                Desktop.SetWallpaper(path);
                WallpaperService.RemovePreviousImages(path);
                AppendLog("last-run.log", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  applied " + path);
                return 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog(ex);
                return 1;
            }
        }

        internal static void WriteErrorLog(Exception ex)
        {
            try
            {
                AppendLog("last-error.log", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }

        // Appends one entry and keeps only the newest lines, so the logs never grow without bound.
        private static void AppendLog(string fileName, string text)
        {
            Directory.CreateDirectory(WallpaperService.StorageFolder);

            string path = Path.Combine(WallpaperService.StorageFolder, fileName);
            File.AppendAllText(path, text + Environment.NewLine);

            string[] lines = File.ReadAllLines(path);
            if (lines.Length <= MaxLogLines)
            {
                return;
            }

            string[] newest = new string[MaxLogLines];
            Array.Copy(lines, lines.Length - MaxLogLines, newest, 0, MaxLogLines);
            File.WriteAllLines(path, newest);
        }

        internal static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return key != null && key.GetValue(RunValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("The sign-in registry key could not be opened.");
                }

                if (enabled)
                {
                    key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" " + SilentArgument);
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                }
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Label captionLabel;
        private readonly Label statusLabel;
        private readonly Button setButton;
        private readonly CheckBox startupCheckBox;
        private readonly CheckBox dailyCheckBox;
        private readonly DateTimePicker dailyTimePicker;
        private DailyImage today;
        private bool applyingSettings;

        internal MainForm()
        {
            Text = "Daily Bing Wallpaper (build " + BuildInfo.Date + ")";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(468, 214);
            AutoScaleMode = AutoScaleMode.Font;

            captionLabel = new Label();
            captionLabel.SetBounds(16, 14, 436, 46);
            captionLabel.Text = "Loading today's wallpaper...";

            statusLabel = new Label();
            statusLabel.SetBounds(16, 62, 436, 20);
            statusLabel.ForeColor = SystemColors.GrayText;
            statusLabel.AutoEllipsis = true;
            statusLabel.Text = "Source: github.com/niumoo/bing-wallpaper";

            setButton = new Button();
            setButton.SetBounds(16, 92, 150, 32);
            setButton.Text = "Set wallpaper";
            setButton.Enabled = false;
            setButton.Click += OnSetClicked;

            startupCheckBox = new CheckBox();
            startupCheckBox.SetBounds(16, 136, 320, 24);
            startupCheckBox.Text = "Refresh at sign-in";
            startupCheckBox.Checked = Program.IsStartupEnabled();
            startupCheckBox.CheckedChanged += OnStartupToggled;

            dailyCheckBox = new CheckBox();
            dailyCheckBox.SetBounds(16, 164, 110, 24);
            dailyCheckBox.Text = "Daily at";
            dailyCheckBox.CheckedChanged += OnDailyToggled;

            dailyTimePicker = new DateTimePicker();
            dailyTimePicker.SetBounds(130, 162, 72, 24);
            dailyTimePicker.Format = DateTimePickerFormat.Custom;
            dailyTimePicker.CustomFormat = "HH:mm";
            dailyTimePicker.ShowUpDown = true;
            dailyTimePicker.Value = DateTime.Today.AddHours(9);
            dailyTimePicker.ValueChanged += OnDailyTimeChanged;

            Controls.Add(captionLabel);
            Controls.Add(statusLabel);
            Controls.Add(setButton);
            Controls.Add(startupCheckBox);
            Controls.Add(dailyCheckBox);
            Controls.Add(dailyTimePicker);

            Load += OnLoaded;
        }

        private async void OnLoaded(object sender, EventArgs e)
        {
            applyingSettings = true;
            try
            {
                dailyCheckBox.Checked = Scheduler.IsEnabled();
            }
            finally
            {
                applyingSettings = false;
            }

            dailyTimePicker.Enabled = dailyCheckBox.Checked;
            await LoadTodayAsync();
        }

        private async Task LoadTodayAsync()
        {
            statusLabel.Text = "Checking for today's wallpaper...";
            try
            {
                today = await WallpaperService.FetchTodayAsync();
                captionLabel.Text = today.Caption;
                statusLabel.Text = "Ready - " + today.Date;
                setButton.Enabled = true;
            }
            catch (Exception ex)
            {
                Program.WriteErrorLog(ex);
                captionLabel.Text = "Couldn't reach the wallpaper list.";
                statusLabel.Text = ex.Message;
            }
        }

        private async void OnSetClicked(object sender, EventArgs e)
        {
            setButton.Enabled = false;
            statusLabel.Text = "Downloading...";
            try
            {
                if (today == null)
                {
                    today = await WallpaperService.FetchTodayAsync();
                }

                string path = await WallpaperService.DownloadAsync(today);
                Desktop.SetWallpaper(path);
                WallpaperService.RemovePreviousImages(path);
                captionLabel.Text = today.Caption;
                statusLabel.Text = "Wallpaper updated - " + today.Date;
            }
            catch (Exception ex)
            {
                Program.WriteErrorLog(ex);
                statusLabel.Text = ex.Message;
            }
            finally
            {
                setButton.Enabled = true;
            }
        }

        private void OnStartupToggled(object sender, EventArgs e)
        {
            try
            {
                Program.SetStartup(startupCheckBox.Checked);
                statusLabel.Text = startupCheckBox.Checked
                    ? "Will refresh at sign-in."
                    : "Sign-in refresh turned off.";
            }
            catch (Exception ex)
            {
                Program.WriteErrorLog(ex);
                statusLabel.Text = ex.Message;
                startupCheckBox.Checked = Program.IsStartupEnabled();
            }
        }

        private void OnDailyToggled(object sender, EventArgs e)
        {
            if (applyingSettings)
            {
                return;
            }

            dailyTimePicker.Enabled = dailyCheckBox.Checked;
            ApplyDailySchedule();
        }

        private void OnDailyTimeChanged(object sender, EventArgs e)
        {
            if (!applyingSettings && dailyCheckBox.Checked)
            {
                ApplyDailySchedule();
            }
        }

        private void ApplyDailySchedule()
        {
            string time = dailyTimePicker.Value.ToString("HH:mm", CultureInfo.InvariantCulture);

            try
            {
                if (dailyCheckBox.Checked)
                {
                    Scheduler.Enable(time);
                    statusLabel.Text = "Daily refresh scheduled for " + time + ".";
                }
                else
                {
                    Scheduler.Disable();
                    statusLabel.Text = "Daily refresh turned off.";
                }
            }
            catch (Exception ex)
            {
                Program.WriteErrorLog(ex);
                statusLabel.Text = ex.Message;

                applyingSettings = true;
                try
                {
                    dailyCheckBox.Checked = Scheduler.IsEnabled();
                }
                finally
                {
                    applyingSettings = false;
                }
            }
        }
    }
}
