using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DailyBingWallpaper
{
    internal static class Shortcuts
    {
        internal static string StartMenuPath
        {
            get { return InPrograms("Daily Bing Wallpaper.lnk"); }
        }

        internal static string DesktopPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "Daily Bing Wallpaper.lnk");
            }
        }

        internal static string ExecutablePath
        {
            get { return Process.GetCurrentProcess().MainModule.FileName; }
        }

        /// <summary>Writes (or refreshes) a shortcut so it points at the target's current location.</summary>
        internal static void Create(string linkPath, string targetPath)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { linkPath });

                Type linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { targetPath });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link,
                    new object[] { Path.GetDirectoryName(targetPath) });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link,
                    new object[] { "Daily Bing Wallpaper" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);

                Marshal.ReleaseComObject(link);
                Marshal.ReleaseComObject(shell);
            }
            catch
            {
                // A shortcut is a convenience; it must never block installation or updates.
            }
        }

        internal static void Remove(string linkPath)
        {
            try
            {
                if (File.Exists(linkPath))
                {
                    File.Delete(linkPath);
                }
            }
            catch
            {
            }
        }

        private static string InPrograms(string fileName)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                fileName);
        }
    }
}
