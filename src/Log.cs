using System;
using System.IO;
using System.Text;

namespace Okna
{
    internal static class Log
    {
        private static readonly object Sync = new object();
        private static readonly string FilePath = ResolvePath();
        private static readonly string ExeFallback =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Okna.log");
        private static readonly string TempFallback =
            Path.Combine(Path.GetTempPath(), "Okna.log");
        private static readonly bool DebugEnabled =
            string.Equals(Environment.GetEnvironmentVariable("OKNA_DEBUG"), "1", StringComparison.Ordinal);

        public static string LogPath
        {
            get { return FilePath; }
        }

        private static string ResolvePath()
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                    return Path.Combine(Path.Combine(local, "Okna"), "okna.log");
            }
            catch { }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Okna.log");
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Debug(string message)
        {
            if (DebugEnabled)
                Write("DEBUG", message);
        }

        private static void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                          " [" + level + "] " + message + Environment.NewLine;
            if (TryWrite(FilePath, line)) return;
            if (TryWrite(ExeFallback, line)) return;
            TryWrite(TempFallback, line);
        }

        private static bool TryWrite(string path, string line)
        {
            try
            {
                lock (Sync)
                {
                    string folder = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder))
                        Directory.CreateDirectory(folder);
                    FileInfo info = new FileInfo(path);
                    if (info.Exists && info.Length > 1024 * 1024)
                    {
                        string old = OldPathFor(path);
                        try { if (File.Exists(old)) File.Delete(old); } catch { }
                        try
                        {
                            File.Move(path, old);
                        }
                        catch
                        {
                            // The rotated target is locked by another process:
                            // truncate the current file instead of losing the
                            // line, and keep appending.
                            try { File.WriteAllText(path, string.Empty); } catch { }
                        }
                        // A build before 1.7.4 used "okna.log.old".
                        string legacy = path + ".old";
                        try { if (File.Exists(legacy)) File.Delete(legacy); } catch { }
                    }
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The rotated name: "okna.log" becomes "okna.old.log", so the file
        /// stays easy to find next to the current one.
        /// </summary>
        internal static string OldPathFor(string path)
        {
            string folder = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(name)) name = "okna";
            string file = name + ".old" + extension;
            return string.IsNullOrEmpty(folder) ? file : Path.Combine(folder, file);
        }
    }
}
