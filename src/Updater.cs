using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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

        public static string PendingInfoFile
        {
            get { return Path.Combine(UpdateDir, "pending.info"); }
        }

        public static string HealthFile
        {
            get { return Path.Combine(UpdateDir, "health.txt"); }
        }

        public static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        /// <summary>
        /// Written by a freshly updated process once it has really started
        /// (engine running, tray created). The watchdog reads it to decide
        /// whether the new build is healthy.
        /// </summary>
        public static void MarkHealthy()
        {
            try
            {
                Directory.CreateDirectory(UpdateDir);
                File.WriteAllText(HealthFile,
                    CurrentLabel + "|" + DateTime.UtcNow.Ticks + "|" + Process.GetCurrentProcess().Id,
                    Encoding.ASCII);
                Log.Info("Update health marker written: " + CurrentLabel);
            }
            catch (Exception ex)
            {
                Log.Debug("Health marker failed: " + ex.Message);
            }
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
            Download(info, null);
        }

        public static void Download(UpdateInfo info, Action<int> progress)
        {
            if (info == null) throw new ArgumentNullException("info");
            Directory.CreateDirectory(UpdateDir);
            string temp = StagedExe + ".part";
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }

            Exception downloadError = null;
            using (WebClient client = new WebClient())
            using (System.Threading.AutoResetEvent done = new System.Threading.AutoResetEvent(false))
            {
                client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                if (progress != null)
                {
                    client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                    {
                        try
                        {
                            int p = e.ProgressPercentage;
                            if (p < 0) p = 0; if (p > 100) p = 100;
                            progress(p);
                        }
                        catch { }
                    };
                }
                client.DownloadFileCompleted += delegate(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
                {
                    downloadError = e.Error;
                    try { done.Set(); } catch { }
                };
                client.DownloadFileAsync(new Uri(info.DownloadUrl), temp);
                done.WaitOne();
            }
            if (downloadError != null)
            {
                // Only the unfinished copy is removed; a previously verified
                // release in StagedExe must stay installable.
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw downloadError;
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
            SavePendingInfo(info);
            if (progress != null) try { progress(100); } catch { }
        }

        internal static void SavePendingInfo(UpdateInfo info)
        {
            SavePendingInfo(info, PendingInfoFile);
        }

        internal static void SavePendingInfo(UpdateInfo info, string path)
        {
            try
            {
                if (info == null || string.IsNullOrEmpty(path)) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Version=" + Format(info.Version));
                sb.AppendLine("PageUrl=" + (info.PageUrl ?? ""));
                string notes = info.Notes ?? "";
                notes = notes.Replace("\r", "").Replace("\n", "\\n");
                sb.AppendLine("Notes=" + notes);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Debug("Pending info save failed: " + ex.Message);
            }
        }

        internal static UpdateInfo LoadPendingInfo()
        {
            return LoadPendingInfo(PendingInfoFile);
        }

        internal static UpdateInfo LoadPendingInfo(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string ver = null, url = null, notes = null;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim();
                    string v = raw.Substring(eq + 1);
                    if (string.Equals(k, "Version", StringComparison.OrdinalIgnoreCase)) ver = v.Trim();
                    else if (string.Equals(k, "PageUrl", StringComparison.OrdinalIgnoreCase)) url = v.Trim();
                    else if (string.Equals(k, "Notes", StringComparison.OrdinalIgnoreCase)) notes = v.Replace("\\n", "\n");
                }
                if (string.IsNullOrEmpty(ver)) return null;
                Version pv;
                if (!Version.TryParse(ver, out pv)) return null;
                UpdateInfo info = new UpdateInfo();
                info.Version = pv;
                info.PageUrl = url;
                info.Notes = notes;
                return info;
            }
            catch { return null; }
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
        /// Removes only an unfinished download. A verified staged release that
        /// is already waiting for installation is left untouched, so a failed
        /// re-check cannot destroy a ready update.
        /// </summary>
        public static void DiscardPartial()
        {
            try { if (File.Exists(StagedExe + ".part")) File.Delete(StagedExe + ".part"); } catch { }
        }

        /// <summary>
        /// Throws away a staged release that the user decided not to install.
        /// </summary>
        public static void Discard()
        {
            try { if (File.Exists(PendingFile)) File.Delete(PendingFile); } catch { }
            try { if (File.Exists(PendingInfoFile)) File.Delete(PendingInfoFile); } catch { }
            try { if (File.Exists(StagedExe)) File.Delete(StagedExe); } catch { }
            try { if (File.Exists(StagedExe + ".part")) File.Delete(StagedExe + ".part"); } catch { }
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

            Version expected = PendingVersion();
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

            Process started;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(target, "--silent --restart");
                start.WorkingDirectory = Path.GetDirectoryName(target);
                start.UseShellExecute = false;
                started = Process.Start(start);
            }
            catch (Exception ex)
            {
                Log.Error("Update restart failed: " + ex.Message);
                RestoreBackup(target, backup);
                return false;
            }

            // The old build stays on disk as the watchdog: it waits for the
            // new process to report that it is healthy, and puts itself back
            // if it never does.
            StartWatchdog(target, backup, expected, started == null ? 0 : started.Id);

            try { File.Delete(PendingFile); } catch { }
            try { if (File.Exists(PendingInfoFile)) File.Delete(PendingInfoFile); } catch { }
            try { File.Delete(StagedExe); } catch { }
            Log.Info("Update applied, restarting");
            return true;
        }

        private static void StartWatchdog(string target, string backup, Version expected, int pid)
        {
            try
            {
                if (!File.Exists(backup)) return;
                ProcessStartInfo info = new ProcessStartInfo(backup);
                info.Arguments = "--watchdog " + Quote(target) + " " + Quote(backup) + " " +
                    Quote(expected == null ? "" : Format(expected)) + " " + pid;
                info.WorkingDirectory = Path.GetDirectoryName(backup);
                info.UseShellExecute = false;
                Process.Start(info);
                Log.Info("Update watchdog started");
            }
            catch (Exception ex)
            {
                Log.Error("Update watchdog failed to start: " + ex.Message);
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "") + "\"";
        }

        private static void RestoreBackup(string target, string backup)
        {
            try
            {
                if (File.Exists(backup)) File.Copy(backup, target, true);
            }
            catch (Exception ex)
            {
                Log.Error("Update rollback failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Runs in the old executable after an update. Waits for the new
        /// process to write the health marker; if it does not appear in time
        /// (or the process dies), the old build is put back and started.
        /// </summary>
        public static bool RunWatchdog(string[] args)
        {
            string target = null, backup = null, expected = null;
            int pid = 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "--watchdog", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 4 < args.Length)
                {
                    target = args[i + 1];
                    backup = args[i + 2];
                    expected = args[i + 3];
                    int.TryParse(args[i + 4], out pid);
                }
                break;
            }

            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(backup))
            {
                Log.Error("Watchdog: bad arguments");
                return false;
            }

            Log.Info("Watchdog waiting for " + expected + " pid=" + pid);
            DateTime start = DateTime.UtcNow;
            bool healthy = false;
            for (int i = 0; i < 150; i++)
            {
                if (HealthIsGood(expected, start))
                {
                    healthy = true;
                    break;
                }
                if (pid > 0 && ProcessExited(pid))
                    break;
                Thread.Sleep(200);
            }

            if (healthy)
            {
                try { if (File.Exists(HealthFile)) File.Delete(HealthFile); } catch { }
                try { if (File.Exists(backup)) File.Delete(backup); } catch { }
                Log.Info("Watchdog: new version is healthy");
                return true;
            }

            Log.Error("Watchdog: new version did not start; rolling back");
            Kill(pid);
            RestoreBackup(target, backup);
            try { if (File.Exists(HealthFile)) File.Delete(HealthFile); } catch { }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(target, "--silent --restart");
                info.WorkingDirectory = Path.GetDirectoryName(target);
                info.UseShellExecute = false;
                Process.Start(info);
                Log.Info("Watchdog: previous version restarted");
            }
            catch (Exception ex)
            {
                Log.Error("Watchdog restart failed: " + ex.Message);
            }
            return false;
        }

        private static bool HealthIsGood(string expected, DateTime start)
        {
            try
            {
                if (!File.Exists(HealthFile)) return false;
                string[] parts = File.ReadAllText(HealthFile).Trim().Split('|');
                if (parts.Length < 2) return false;
                if (!string.IsNullOrEmpty(expected) &&
                    !string.Equals(parts[0], "v" + expected, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(parts[0], expected, StringComparison.OrdinalIgnoreCase))
                    return false;
                long ticks;
                if (!long.TryParse(parts[1], out ticks)) return false;
                return ticks >= start.Ticks - TimeSpan.FromSeconds(3).Ticks;
            }
            catch
            {
                return false;
            }
        }

        private static bool ProcessExited(int pid)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                return p.HasExited;
            }
            catch
            {
                return true;
            }
        }

        private static void Kill(int pid)
        {
            if (pid <= 0) return;
            try
            {
                Process p = Process.GetProcessById(pid);
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch { }
        }

        /// <summary>
        /// Removes leftovers of a previous update. Called once at startup. A
        /// young .old file is left alone: the watchdog may still need it.
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
                        try
                        {
                            FileInfo info = new FileInfo(old);
                            if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromMinutes(10))
                                File.Delete(old);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try
            {
                if (!File.Exists(PendingFile) && File.Exists(StagedExe)) File.Delete(StagedExe);
                if (!File.Exists(PendingFile) && File.Exists(StagedExe + ".part"))
                    File.Delete(StagedExe + ".part");
                if (!File.Exists(PendingFile) && File.Exists(PendingInfoFile))
                    File.Delete(PendingInfoFile);
                if (File.Exists(HealthFile) &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(HealthFile) > TimeSpan.FromDays(1))
                    File.Delete(HealthFile);
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
