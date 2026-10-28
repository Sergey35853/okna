using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Okna
{
    /// <summary>
    /// A release on GitHub that is newer than the running build.
    /// </summary>
    internal sealed class UpdateInfo
    {
        public Version Version;
        public string DownloadUrl;
        public string Sha256Url;
        public string PageUrl;
        public string Notes;
    }

    /// <summary>
    /// Self-update through GitHub Releases. The release is expected to carry
    /// Okna.exe and Okna.exe.sha256; the downloaded file is verified before it
    /// is allowed anywhere near the running program.
    /// </summary>
    internal static class Updater
    {
        public const string Owner = "Sergey35853";
        public const string Repo = "okna";

        private const string ExeAsset = "Okna.exe";
        private const string ShaAsset = "Okna.exe.sha256";
        private const string UserAgent = "Okna-Updater";

        static Updater()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            try { ServicePointManager.Expect100Continue = false; } catch { }
        }

        public static Version CurrentVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string CurrentLabel
        {
            get { return "v" + Format(CurrentVersion); }
        }

        /// <summary>
        /// "1.3.1" or "1.3.1.2", whichever the version actually carries.
        /// </summary>
        public static string Format(Version version)
        {
            if (version == null) return "";
            return version.Revision > 0 ? version.ToString(4) : version.ToString(3);
        }

        /// <summary>
        /// Overridable so the updater can be tested against a local server.
        /// </summary>
        public static string ApiRoot
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("OKNA_UPDATE_API");
                if (!string.IsNullOrEmpty(custom)) return custom.TrimEnd('/');
                return "https://api.github.com/repos/" + Owner + "/" + Repo;
            }
        }

        public static string UpdateDir
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
                return Path.Combine(Path.Combine(local, "Okna"), "update");
            }
        }

        public static string StagedExe
        {
            get { return Path.Combine(UpdateDir, ExeAsset); }
        }

        public static string PendingFile
        {
            get { return Path.Combine(UpdateDir, "pending.txt"); }
        }

        public static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        /// <summary>
        /// Asks GitHub for the latest release. Returns null when the running
        /// build is already current. Throws when the answer cannot be used.
        /// </summary>
        public static UpdateInfo Check()
        {
            string json = GetString(ApiRoot + "/releases/latest");
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 4 * 1024 * 1024;
            Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(json);
            if (root == null) throw new InvalidDataException("Пустой ответ GitHub");

            string tag = Get<string>(root, "tag_name");
            if (string.IsNullOrEmpty(tag)) throw new InvalidDataException("В релизе нет тега");

            Version remote;
            string clean = tag.Trim();
            if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(1);
            if (!Version.TryParse(clean, out remote))
                throw new InvalidDataException("Непонятный тег релиза: " + tag);
            if (remote <= CurrentVersion) return null;

            UpdateInfo info = new UpdateInfo();
            info.Version = remote;
            info.PageUrl = Get<string>(root, "html_url");
            info.Notes = Get<string>(root, "body");

            object assetsValue = Get<object>(root, "assets");
            IEnumerable assets = assetsValue as IEnumerable;
            if (assets != null)
            {
                foreach (object item in assets)
                {
                    IDictionary<string, object> asset = item as IDictionary<string, object>;
                    if (asset == null) continue;
                    object nameValue, urlValue;
                    asset.TryGetValue("name", out nameValue);
                    asset.TryGetValue("browser_download_url", out urlValue);
                    string name = nameValue as string;
                    string url = urlValue as string;
                    if (string.Equals(name, ExeAsset, StringComparison.OrdinalIgnoreCase))
                        info.DownloadUrl = url;
                    else if (string.Equals(name, ShaAsset, StringComparison.OrdinalIgnoreCase))
                        info.Sha256Url = url;
                }
            }

            if (string.IsNullOrEmpty(info.DownloadUrl))
                throw new InvalidDataException("В релизе нет файла " + ExeAsset);
            if (string.IsNullOrEmpty(info.Sha256Url))
                throw new InvalidDataException("В релизе нет контрольной суммы " + ShaAsset);
            return info;
        }

        /// <summary>
        /// Downloads and verifies a release, leaving it staged for Apply.
        /// </summary>
        public static void Download(UpdateInfo info)
        {
            Directory.CreateDirectory(UpdateDir);
            string temp = StagedExe + ".part";
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }

            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                client.DownloadFile(info.DownloadUrl, temp);
            }

            string expected = ReadChecksum(info.Sha256Url);
            string actual = Sha256(temp);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(temp); } catch { }
                throw new InvalidDataException("Контрольная сумма не совпала");
            }

            if (File.Exists(StagedExe)) File.Delete(StagedExe);
            File.Move(temp, StagedExe);
            File.WriteAllText(PendingFile, Format(info.Version), Encoding.ASCII);
        }

        /// <summary>
        /// True when a verified release is waiting to be installed.
        /// </summary>
        public static bool HasPending
        {
            get
            {
                try { return File.Exists(StagedExe) && File.Exists(PendingFile); }
                catch { return false; }
            }
        }

        public static Version PendingVersion()
        {
            try
            {
                if (!HasPending) return null;
                Version v;
                return Version.TryParse(File.ReadAllText(PendingFile).Trim(), out v) ? v : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Puts the staged build in place and starts it. The running image is
        /// renamed first, which Windows allows; the new copy then takes its
        /// place. Returns true when the new process was started.
        /// </summary>
        public static bool Apply()
        {
            if (!HasPending) return false;

            string target = ExePath;
            if (string.IsNullOrEmpty(target) || !File.Exists(target))
            {
                Log.Error("Update apply: executable path is unusable");
                return false;
            }

            string backup = target + ".old";
            try
            {
                if (File.Exists(backup)) File.Delete(backup);
            }
            catch { }

            try
            {
                File.Move(target, backup);
                File.Copy(StagedExe, target, true);
            }
            catch (Exception ex)
            {
                Log.Error("Update apply failed: " + ex.Message);
                try
                {
                    if (!File.Exists(target) && File.Exists(backup)) File.Move(backup, target);
                }
                catch { }
                return false;
            }

            try
            {
                ProcessStartInfo start = new ProcessStartInfo(target, "--silent --restart");
                start.WorkingDirectory = Path.GetDirectoryName(target);
                start.UseShellExecute = false;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                Log.Error("Update restart failed: " + ex.Message);
                return false;
            }

            try { File.Delete(PendingFile); } catch { }
            try { File.Delete(StagedExe); } catch { }
            Log.Info("Update applied, restarting");
            return true;
        }

        /// <summary>
        /// Removes leftovers of a previous update. Called once at startup.
        /// </summary>
        public static void Cleanup()
        {
            try
            {
                string dir = Path.GetDirectoryName(ExePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    foreach (string old in Directory.GetFiles(dir, "Okna.exe.old"))
                    {
                        try { File.Delete(old); } catch { }
                    }
                }
            }
            catch { }

            try
            {
                if (!File.Exists(PendingFile) && File.Exists(StagedExe)) File.Delete(StagedExe);
                if (!File.Exists(PendingFile) && File.Exists(StagedExe + ".part"))
                    File.Delete(StagedExe + ".part");
            }
            catch { }
        }

        private static string ReadChecksum(string url)
        {
            string text = GetString(url).Trim();
            if (text.Length == 0) throw new InvalidDataException("Пустая контрольная сумма");
            int end = text.IndexOfAny(new char[] { ' ', '\t', '\r', '\n' });
            string hash = end < 0 ? text : text.Substring(0, end);
            if (hash.Length != 64) throw new InvalidDataException("Контрольная сумма неверного вида");
            return hash;
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string GetString(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 20000;
            request.ReadWriteTimeout = 30000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static T Get<T>(Dictionary<string, object> map, string key) where T : class
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value)) return null;
            return value as T;
        }
    }
}
