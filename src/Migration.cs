using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace GhostHunterLauncher
{
    /// <summary>
    /// Carries a "Ghost Hunter" install over to the "Who Haunts" name, so the rename costs players nothing: the
    /// downloaded game moves to the new folder (no re-download), the game's saved settings and profile follow it,
    /// and the shortcuts and Apps &amp; features entry take the new name. Every step only runs while there's
    /// something old to carry over and nothing new in its way, so it's safe to run on every start.
    /// </summary>
    static class Migration
    {
        const string LegacyInstallFolder = "GhostHunter";
        const string LegacyShortcut = "Ghost Hunter.lnk";
        const string Shortcut = "Who Haunts.lnk";

        // Unity keeps PlayerPrefs under HKCU\Software\<Company Name>\<Product Name>.
        const string LegacyPrefsKey = @"Software\DefaultCompany\Ghost Hunter";
        const string PrefsKey = @"Software\DefaultCompany\Who Haunts";

        // Inno Setup's per-user uninstall entry (AppId in installer/GhostHunterLauncher.iss, plus "_is1").
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F3B2C1E-8D4A-4E7B-9C5F-2A1D0E8B7F43}_is1";

        public static void Run()
        {
            Try(MoveInstall);
            Try(CopyGamePrefs);
            Try(RenameShortcuts);
            Try(RenameUninstallEntry);
        }

        static void MoveInstall()
        {
            var legacy = Path.Combine(Path.GetDirectoryName(LauncherConfig.InstallRoot), LegacyInstallFolder);
            if (Directory.Exists(legacy) && !Directory.Exists(LauncherConfig.InstallRoot))
                Directory.Move(legacy, LauncherConfig.InstallRoot);
        }

        static void CopyGamePrefs()
        {
            using (var existing = Registry.CurrentUser.OpenSubKey(PrefsKey))
                if (existing != null) return; // the renamed game has already saved its own

            // Copied raw: Unity saves floats as 8 bytes under a DWORD type, which RegistryKey can't round-trip.
            using (var legacy = Registry.CurrentUser.OpenSubKey(LegacyPrefsKey))
            {
                if (legacy == null) return;
                int error;
                using (var target = Registry.CurrentUser.CreateSubKey(PrefsKey))
                    error = CopyValues(legacy.Handle, target.Handle);
                if (error != 0)
                {
                    // Drop the half copy so the next start tries again instead of seeing a key that "already exists".
                    Registry.CurrentUser.DeleteSubKeyTree(PrefsKey, false);
                    throw new Win32Exception(error);
                }
            }
        }

        /// <summary>Copies every value byte for byte with its original type. Returns a Win32 error code, 0 on success.</summary>
        static int CopyValues(SafeRegistryHandle source, SafeRegistryHandle target)
        {
            const int NoMoreItems = 259, MoreData = 234;
            var name = new StringBuilder(256);
            var data = new byte[1024];
            for (int index = 0; ; )
            {
                int nameLength = name.Capacity, dataLength = data.Length, type;
                int error = RegEnumValue(source, index, name, ref nameLength, IntPtr.Zero, out type, data, ref dataLength);
                if (error == NoMoreItems) return 0;
                if (error == MoreData)
                {
                    name.Capacity = Math.Max(name.Capacity, Math.Min(16384, name.Capacity * 4));
                    data = new byte[Math.Max(data.Length * 4, dataLength)];
                    continue; // same index again with bigger buffers
                }
                if (error != 0) return error;
                error = RegSetValueEx(target, name.ToString(0, nameLength), 0, type, data, dataLength);
                if (error != 0) return error;
                index++;
            }
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegEnumValue(SafeRegistryHandle key, int index, StringBuilder name, ref int nameLength,
            IntPtr reserved, out int type, byte[] data, ref int dataLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegSetValueEx(SafeRegistryHandle key, string name, int reserved, int type, byte[] data, int dataLength);

        static void RenameShortcuts()
        {
            foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory })
            {
                var dir = Environment.GetFolderPath(folder);
                var legacy = Path.Combine(dir, LegacyShortcut);
                var renamed = Path.Combine(dir, Shortcut);
                if (File.Exists(legacy) && !File.Exists(renamed)) File.Move(legacy, renamed);
            }
        }

        static void RenameUninstallEntry()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey, true))
            {
                if (key == null) return;
                if ((key.GetValue("DisplayName") as string) == "Ghost Hunter") key.SetValue("DisplayName", "Who Haunts");
                if ((key.GetValue("Publisher") as string) == "Ghost Hunter") key.SetValue("Publisher", "Who Haunts");
            }
        }

        // A step that fails (a file in use, a locked key) is retried on the next start; it never blocks playing.
        static void Try(Action step)
        {
            try { step(); } catch { }
        }
    }
}
