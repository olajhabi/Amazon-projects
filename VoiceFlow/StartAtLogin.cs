using System;
using System.IO;
using System.Windows.Forms;

namespace VoiceFlow
{
    /// <summary>
    /// Manages the Windows Startup folder shortcut for start-at-login.
    /// Uses a .lnk via WScript.Shell — no registry needed.
    /// </summary>
    public static class StartAtLogin
    {
        private static readonly string StartupFolder =
            Environment.GetFolderPath(Environment.SpecialFolder.Startup);

        private static readonly string ShortcutPath =
            Path.Combine(StartupFolder, "VoiceFlow.lnk");

        private static readonly string ExePath =
            Application.ExecutablePath;

        public static bool IsEnabled() => File.Exists(ShortcutPath);

        public static void Enable()
        {
            if (IsEnabled()) return;
            CreateShortcut(ShortcutPath, ExePath);
        }

        public static void Disable()
        {
            if (File.Exists(ShortcutPath))
                File.Delete(ShortcutPath);
        }

        public static void Toggle()
        {
            if (IsEnabled()) Disable();
            else Enable();
        }

        private static void CreateShortcut(string shortcutPath, string targetPath)
        {
            // Use Windows Script Host COM to create the .lnk
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Description = "VoiceFlow — system-wide dictation";
            shortcut.Save();
        }
    }
}
