using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DailyBingWallpaper
{
    internal sealed class DailyImage
    {
        internal string Date { get; set; }
        internal string Caption { get; set; }
        internal string Url { get; set; }
    }

    /// <summary>
    /// Reads the daily Bing image from github.com/niumoo/bing-wallpaper and stores it locally.
    /// </summary>
    internal static class WallpaperService
    {
        internal const string ReadmeUrl =
            "https://raw.githubusercontent.com/niumoo/bing-wallpaper/main/README.md";

        private static readonly HttpClient Client = CreateClient();

        internal static string StorageFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DailyBingWallpaper");
            }
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DailyBingWallpaper/1.0");
            return client;
        }

        internal static async Task<DailyImage> FetchTodayAsync()
        {
            string readme = await Client.GetStringAsync(ReadmeUrl).ConfigureAwait(false);
            return Parse(readme);
        }

        internal static DailyImage Parse(string readme)
        {
            if (string.IsNullOrEmpty(readme))
            {
                throw new InvalidDataException("The wallpaper list was empty.");
            }

            Match link = Regex.Match(
                readme,
                @"Today:\s*\[(?<caption>[^\]]*)\]\((?<url>https?://[^)\s]+)\)");

            if (!link.Success)
            {
                throw new InvalidDataException(
                    "No \"Today:\" wallpaper link was found in the wallpaper list.");
            }

            DailyImage image = new DailyImage();
            image.Caption = WebUtility.HtmlDecode(link.Groups["caption"].Value).Trim();
            image.Url = WebUtility.HtmlDecode(link.Groups["url"].Value).Trim();

            Match date = Regex.Match(readme, @"\b(\d{4}-\d{2}-\d{2})\b");
            image.Date = date.Success
                ? date.Groups[1].Value
                : DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return image;
        }

        /// <summary>Downloads today's image once and returns the local JPEG path.</summary>
        internal static async Task<string> DownloadAsync(DailyImage image)
        {
            if (image == null)
            {
                throw new ArgumentNullException("image");
            }

            Directory.CreateDirectory(StorageFolder);
            string path = Path.Combine(StorageFolder, BuildFileName(image));

            if (IsUsableJpeg(path))
            {
                return path;
            }

            byte[] bytes = await Client.GetByteArrayAsync(image.Url).ConfigureAwait(false);
            if (!IsUsableJpeg(bytes))
            {
                throw new InvalidDataException("The downloaded file was not a valid JPEG image.");
            }

            string partial = path + ".part";
            File.WriteAllBytes(partial, bytes);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(partial, path);
            return path;
        }

        // Called after the new image is applied: keeps only that file, including
        // sweeping partial downloads left behind by an interrupted run.
        internal static void RemovePreviousImages(string keepPath)
        {
            string keepName = Path.GetFileName(keepPath);
            foreach (string file in Directory.GetFiles(StorageFolder, "bing-*"))
            {
                if (string.Equals(Path.GetFileName(file), keepName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Still held open; the next run removes it.
                }
            }
        }

        private static string BuildFileName(DailyImage image)
        {
            Match id = Regex.Match(image.Url, @"id=OHR\.(?<id>[A-Za-z0-9]+)");
            string suffix = id.Success ? id.Groups["id"].Value : "daily";
            return "bing-" + image.Date + "-" + suffix + ".jpg";
        }

        // Heads up: a truncated download still starts with the JPEG marker,
        // so require a plausible size as well before trusting a cached file.
        private static bool IsUsableJpeg(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 50000)
            {
                return false;
            }

            byte[] head = new byte[2];
            using (FileStream stream = File.OpenRead(path))
            {
                if (stream.Read(head, 0, 2) != 2)
                {
                    return false;
                }
            }

            return head[0] == 0xFF && head[1] == 0xD8;
        }

        private static bool IsUsableJpeg(byte[] bytes)
        {
            return bytes != null
                && bytes.Length > 50000
                && bytes[0] == 0xFF
                && bytes[1] == 0xD8;
        }
    }
}
