using System;
using System.Threading;
using System.Windows.Forms;

namespace GhostHunterLauncher
{
    static class Program
    {
        // Also named in installer/GhostHunterLauncher.iss (AppMutex) so setup can ask to close the launcher.
        const string MutexName = "GhostHunterLauncher.SingleInstance";

        [STAThread]
        static void Main(string[] args)
        {
            using (var mutex = new Mutex(false, MutexName))
            {
                // Right after a self-update the previous launcher is still shutting down, so wait for it.
                var wait = args.Contains(LauncherUpdater.UpdatedArg) ? TimeSpan.FromSeconds(15) : TimeSpan.Zero;
                bool owned;
                try { owned = mutex.WaitOne(wait); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned) return;

                try
                {
                    Migration.Run();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new LauncherForm());
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }
    }
}
