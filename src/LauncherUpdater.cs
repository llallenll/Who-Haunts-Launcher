using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExtraLifeLauncher
{
    /// <summary>
    /// Replaces the running launcher exe with a newer release. Windows lets a running exe be renamed but not
    /// overwritten, so the current exe becomes .old, the new one takes its name, and the new one is started.
    /// </summary>
    static class LauncherUpdater
    {
        public const string UpdatedArg = "--updated";
        public const string SkipArg = "--no-self-update";

        static string ExePath { get { return Application.ExecutablePath; } }
        static string NewPath { get { return ExePath + ".new"; } }
        static string OldPath { get { return ExePath + ".old"; } }

        public static Version CurrentVersion
        {
            get { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); }
        }

        /// <summary>Removes what the previous self-update left behind. The old process may still be exiting, so retry briefly.</summary>
        public static async Task CleanupAsync()
        {
            Installer.TryDelete(NewPath);
            for (var attempt = 0; attempt < 10 && File.Exists(OldPath); attempt++)
            {
                Installer.TryDelete(OldPath);
                if (File.Exists(OldPath)) await Task.Delay(300);
            }
        }

        /// <summary>
        /// Returns true when a newer launcher was installed and started, meaning this instance should close.
        /// Any failure is swallowed: an outdated launcher still works, so it never blocks playing.
        /// </summary>
        public static async Task<bool> TryUpdateAsync(Action<string> reportStatus)
        {
            if (Environment.GetCommandLineArgs().Contains(SkipArg)) return false;

            ReleaseInfo release;
            try { release = await GitHubReleases.GetLatestLauncherAsync(); }
            catch { return false; }
            if (release == null) return false;

            Version available;
            if (!Version.TryParse(release.Tag.TrimStart('v', 'V'), out available)) return false;
            if (Normalize(available) <= CurrentVersion) return false;

            try
            {
                reportStatus("Updating launcher to " + release.Tag + "…");
                var progress = new Progress<DownloadProgress>(p =>
                {
                    if (p.Total > 0) reportStatus("Updating launcher to " + release.Tag + "… " + (int)(100.0 * p.Received / p.Total) + "%");
                });
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
                    await GitHubReleases.DownloadAsync(release, NewPath, progress, cts.Token);

                Installer.TryDelete(OldPath);
                File.Move(ExePath, OldPath);
                try { File.Move(NewPath, ExePath); }
                catch
                {
                    File.Move(OldPath, ExePath); // put the working launcher back
                    throw;
                }

                Process.Start(new ProcessStartInfo(ExePath, UpdatedArg) { UseShellExecute = false });
                return true;
            }
            catch
            {
                Installer.TryDelete(NewPath);
                return false;
            }
        }

        // Compare on major.minor.build so "1.2" and "1.2.0.0" are equal.
        static Version Normalize(Version v)
        {
            return new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
        }
    }

    static class ArrayExtensions
    {
        public static bool Contains(this string[] values, string value)
        {
            return Array.IndexOf(values, value) >= 0;
        }
    }
}
