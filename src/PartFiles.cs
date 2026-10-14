using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;

namespace RoboticsMod
{
    /// <summary>
    /// The part definitions (config, meshes, icons) ship inside this DLL. Flyout loads mod parts from
    /// %USERPROFILE%\AppData\LocalLow\Stonext Games\Flyout\Mods\Parts, so they are written there on startup,
    /// before the game reads that folder. Files are only rewritten when their content changed.
    /// </summary>
    public static class PartFiles
    {
        const string Prefix = "parts/";

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        static string LocalLow()
        {
            try
            {
                if (SHGetKnownFolderPath(new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16"), 0, IntPtr.Zero, out var p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(Path.GetDirectoryName(local.TrimEnd('\\', '/')) ?? local, "LocalLow");
        }

        public static string PartsRoot => Path.Combine(LocalLow(), "Stonext Games", "Flyout", "Mods", "Parts");

        public static void Install()
        {
            try
            {
                string root = PartsRoot;
                var asm = Assembly.GetExecutingAssembly();
                int written = 0, total = 0;
                foreach (string res in asm.GetManifestResourceNames())
                {
                    string rel = res.Replace('\\', '/');
                    if (!rel.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    rel = rel.Substring(Prefix.Length);
                    if (rel.Contains("..")) continue;
                    total++;
                    byte[] data;
                    using (var s = asm.GetManifestResourceStream(res))
                    using (var m = new MemoryStream())
                    {
                        if (s == null) continue;
                        s.CopyTo(m);
                        data = m.ToArray();
                    }
                    string target = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(target) && Same(File.ReadAllBytes(target), data)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, data);
                    written++;
                }
                MelonLogger.Msg(written == 0 ? $"Robotics parts up to date ({total} files)" : $"Installed {written} of {total} robotics part files to {root}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("Could not install the robotics part files: " + ex.Message);
            }
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
