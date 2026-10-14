using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("KSP-Like Robotics v1 - Optional Installer")]
[assembly: AssemblyProduct("KSP-Like Robotics")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// Standalone .NET Framework installer. The mod DLL and its part files are the only embedded payload.
internal static class OptionalInstaller
{
    internal const string Title = "KSP-Like Robotics v1 - Optional Installer";
    internal const string LoaderUrl = "https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3";
    internal const string DllName = "RoboticsMod.dll";
    internal const string PartsPrefix = "parts/";
    internal static IEnumerable<string> Values(string text, string key)
    {
        foreach (Match m in Regex.Matches(text, "\"" + Regex.Escape(key) + "\"\\s+\"((?:\\\\.|[^\"\\\\])*)\""))
            yield return m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
    }
    internal static bool IsFlyout(string folder)
    {
        return !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "Flyout.exe"));
    }
    internal static List<string> FindInLibraries(IEnumerable<string> steamRoots)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in steamRoots.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            libraries.Add(root);
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            try
            {
                if (!File.Exists(vdf)) continue;
                string text = File.ReadAllText(vdf);
                foreach (string path in Values(text, "path")) libraries.Add(path);
                // Old Steam library list format uses numbered key/value entries.
                foreach (Match m in Regex.Matches(text, "\"[0-9]+\"\\s+\"((?:\\\\.|[^\"\\\\])*)\""))
                {
                    string path = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Path.IsPathRooted(path)) libraries.Add(path);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string library in libraries)
        {
            string steamapps = Path.Combine(library, "steamapps");
            try
            {
                if (!Directory.Exists(steamapps)) continue;
                foreach (string manifest in Directory.GetFiles(steamapps, "appmanifest_*.acf"))
                {
                    string text = File.ReadAllText(manifest);
                    if (!Values(text, "name").Any(n => n.Equals("Flyout", StringComparison.OrdinalIgnoreCase))) continue;
                    string installDir = Values(text, "installdir").FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(installDir)) continue;
                    string common = Path.GetFullPath(Path.Combine(steamapps, "common")) + Path.DirectorySeparatorChar;
                    string candidate = Path.GetFullPath(Path.Combine(common, installDir));
                    if (candidate.StartsWith(common, StringComparison.OrdinalIgnoreCase) && IsFlyout(candidate)) found.Add(candidate);
                }
                string standard = Path.Combine(steamapps, "common", "Flyout");
                if (IsFlyout(standard)) found.Add(standard);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
        return found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }
    internal static List<string> Detect()
    {
        var roots = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                try
                {
                    using (var registry = RegistryKey.OpenBaseKey(hive, view))
                    using (var key = registry.OpenSubKey(@"Software\Valve\Steam"))
                        if (key != null)
                            foreach (string value in new[] { "SteamPath", "InstallPath" })
                                roots.Add(key.GetValue(value) as string);
                }
                catch (System.Security.SecurityException) { }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
        return FindInLibraries(roots);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    // Flyout reads mod parts from <LocalLow>\Stonext Games\Flyout\Mods\Parts (Unity's persistentDataPath).
    internal static string DefaultPartsRoot()
    {
        string localLow = null;
        try
        {
            IntPtr p;
            if (SHGetKnownFolderPath(new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16"), 0, IntPtr.Zero, out p) == 0)
            {
                localLow = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
            }
        }
        catch (Exception) { }
        if (string.IsNullOrEmpty(localLow))
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).TrimEnd('\\');
            localLow = Path.Combine(Path.GetDirectoryName(local), "LocalLow");
        }
        return Path.Combine(localLow, "Stonext Games", "Flyout", "Mods", "Parts");
    }

    internal static byte[] Resource(string name)
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var buffer = new MemoryStream())
        {
            if (stream == null) throw new IOException("The installer payload is missing. Download the installer again.");
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
    internal static byte[] Payload() { return Resource(DllName); }
    internal static List<string> PartResources()
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.StartsWith(PartsPrefix, StringComparison.Ordinal)).ToList();
    }

    internal static void WriteVerified(string target, byte[] payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        string temporary = Path.Combine(Path.GetDirectoryName(target), ".KspLikeRobotics-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, payload);
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
            using (var sha = SHA256.Create())
                if (!sha.ComputeHash(File.ReadAllBytes(target)).SequenceEqual(sha.ComputeHash(payload)))
                    throw new IOException("Installed file failed verification: " + target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static string Install(string folder, bool checkRunning, string partsRoot)
    {
        if (!IsFlyout(folder)) throw new IOException("Select the folder containing Flyout.exe.");
        if (!File.Exists(Path.Combine(folder, "MelonLoader", "net6", "MelonLoader.dll")))
            throw new IOException("Install MelonLoader 0.7.3 first, start Flyout once to finish loader setup, then run this installer again.");
        if (checkRunning && Process.GetProcessesByName("Flyout").Length > 0)
            throw new IOException("Close Flyout before installing.");
        byte[] payload = Payload();
        var parts = PartResources();
        if (parts.Count == 0) throw new IOException("The installer payload is missing the part files. Download the installer again.");
        string mods = Path.Combine(folder, "Mods");
        string target = Path.Combine(mods, DllName);
        Directory.CreateDirectory(mods);
        string backup = null;
        if (File.Exists(target))
        {
            string backupDir = Path.Combine(folder, "UserData", "KSP-Like-Robotics-Backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(backupDir);
            backup = Path.Combine(backupDir, DllName);
            File.Copy(target, backup, false);
        }
        WriteVerified(target, payload);
        // Part files: config, meshes and icons. The mod also refreshes these itself on every start.
        string fullRoot = Path.GetFullPath(partsRoot) + Path.DirectorySeparatorChar;
        foreach (string res in parts)
        {
            string rel = res.Substring(PartsPrefix.Length).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string file = Path.GetFullPath(Path.Combine(partsRoot, rel));
            if (!file.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) continue;
            WriteVerified(file, Resource(res));
        }
        return "Installed v1.0.0 to:\n" + target + "\n\nPart files installed to:\n" + partsRoot +
            "\n\nStart Flyout and open the ROB (Robotics) tab in the hangar." +
            (backup == null ? "" : "\n\nPrevious mod backed up to:\n" + backup);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest();
        if (args.Contains("--detect-only"))
        {
            File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installer-detected-paths.txt"), Detect());
            return 0;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerWindow());
        return 0;
    }
    private static int SelfTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "FlyoutOptionalInstallerTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            string steam = Path.Combine(root, "Steam"), library = Path.Combine(root, "Other Library");
            string apps = Path.Combine(library, "steamapps"), game = Path.Combine(apps, "common", "Flyout");
            string partsRoot = Path.Combine(root, "LocalLow", "Stonext Games", "Flyout", "Mods", "Parts");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
            Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "Flyout.exe"), "fixture");
            string encoded = library.Replace("\\", "\\\\");
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" { \"path\" \"" + encoded + "\" } }");
            File.WriteAllText(Path.Combine(apps, "appmanifest_1.acf"), "\"AppState\" { \"name\" \"Flyout\" \"installdir\" \"Flyout\" }");
            if (!FindInLibraries(new[] { steam }).Contains(game)) throw new Exception("Multi-library detection failed.");
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" \"" + encoded + "\" }");
            if (!FindInLibraries(new[] { steam }).Contains(game)) throw new Exception("Legacy-library detection failed.");
            bool rejected = false;
            try { Install(game, false, partsRoot); } catch (IOException) { rejected = true; }
            if (!rejected) throw new Exception("Missing-loader guard failed.");
            Directory.CreateDirectory(Path.Combine(game, "MelonLoader", "net6"));
            File.WriteAllText(Path.Combine(game, "MelonLoader", "net6", "MelonLoader.dll"), "fixture-loader");
            Install(game, false, partsRoot);
            string dll = Path.Combine(game, "Mods", DllName);
            if (!File.ReadAllBytes(dll).SequenceEqual(Payload())) throw new Exception("Payload installation failed.");
            foreach (string folder in new[] { "RoboticsHydraulicCylinder", "RoboticsTelescopicCylinder", "RoboticsKAL1000" })
            {
                if (!File.Exists(Path.Combine(partsRoot, folder, "config.txt"))) throw new Exception("Part config missing: " + folder);
                if (!File.Exists(Path.Combine(partsRoot, folder, "icon.png"))) throw new Exception("Part icon missing: " + folder);
                if (!File.Exists(Path.Combine(partsRoot, folder, "Model", folder + ".obj"))) throw new Exception("Part mesh missing: " + folder);
            }
            File.WriteAllText(dll, "previous-version");
            Install(game, false, partsRoot);
            var backups = Directory.GetFiles(Path.Combine(game, "UserData", "KSP-Like-Robotics-Backups"), DllName, SearchOption.AllDirectories);
            if (backups.Length != 1 || File.ReadAllText(backups[0]) != "previous-version") throw new Exception("Upgrade backup failed.");
            rejected = false;
            try { Install(root, false, partsRoot); } catch (IOException) { rejected = true; }
            if (!rejected) throw new Exception("Wrong-folder guard failed.");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installer-self-test.txt"), "PASS: new/legacy Steam libraries, missing loader, fresh install, part files, upgrade, backup, payload integrity, wrong-folder guard. Default parts folder: " + DefaultPartsRoot() + "\n");
            return 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installer-self-test.txt"), "FAIL: " + e);
            return 1;
        }
        finally
        {
            // Only the unique fixture directory created by this invocation is removed.
            if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}

internal sealed class InstallerWindow : Form
{
    private readonly ComboBox path = new ComboBox();
    public InstallerWindow()
    {
        Text = OptionalInstaller.Title;
        ClientSize = new Size(620, 270);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        Controls.Add(new Label { Text = "Optional installer for KSP-Like Robotics v1", AutoSize = false, Bounds = new Rectangle(20, 20, 580, 30), Font = new Font(Font, FontStyle.Bold) });
        Controls.Add(new Label { Text = "Installs the mod DLL into Flyout's Mods folder and the part files into your Flyout\nuser folder. Requires MelonLoader 0.7.3. Close Flyout first; an older DLL is backed up.", Bounds = new Rectangle(20, 60, 580, 55) });
        path.Bounds = new Rectangle(20, 135, 475, 30);
        path.DropDownStyle = ComboBoxStyle.DropDown;
        foreach (string folder in OptionalInstaller.Detect()) path.Items.Add(folder);
        if (path.Items.Count > 0) path.SelectedIndex = 0;
        Controls.Add(path);
        var browse = new Button { Text = "Browse...", Bounds = new Rectangle(505, 132, 95, 32) };
        browse.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "Select the folder containing Flyout.exe", ShowNewFolderButton = false }) if (dialog.ShowDialog(this) == DialogResult.OK) path.Text = dialog.SelectedPath; };
        Controls.Add(browse);
        var loader = new LinkLabel { Text = "Get MelonLoader", Bounds = new Rectangle(20, 185, 190, 28) };
        loader.LinkClicked += delegate { Process.Start(new ProcessStartInfo(OptionalInstaller.LoaderUrl) { UseShellExecute = true }); };
        Controls.Add(loader);
        var install = new Button { Text = "Install mod", Bounds = new Rectangle(360, 215, 115, 35) };
        install.Click += delegate
        {
            try { MessageBox.Show(this, OptionalInstaller.Install(path.Text.Trim().Trim('"'), true, OptionalInstaller.DefaultPartsRoot()), "Installation complete", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch (Exception e) { MessageBox.Show(this, e.Message + "\n\nIf this folder needs administrator access, rerun the installer as administrator or use the manual ZIP.", "Installation could not finish", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        Controls.Add(install);
        var close = new Button { Text = "Close", Bounds = new Rectangle(485, 215, 115, 35) };
        close.Click += delegate { Close(); };
        Controls.Add(close);
        AcceptButton = install;
        CancelButton = close;
    }
}
