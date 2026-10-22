using System;
using System.Threading;
using System.Windows.Forms;

namespace ExtraLifeLauncher
{
    static class Program
    {
        // Also named in installer/ExtraLifeLauncher.iss (AppMutex) so setup can ask to close the launcher.
        // Unchanged since "Ghost Hunter", so a new setup also sees an older launcher running.
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
