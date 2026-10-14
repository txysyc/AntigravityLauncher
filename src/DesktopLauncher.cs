using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class DesktopLauncher
{
    internal const string ApplicationFileName = "Antigravity.exe";
    internal const int ProxyConnectTimeoutMilliseconds = 3000;
    private const int StartupObservationMilliseconds = 3000;
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    [STAThread]
    private static int Main(string[] args)
    {
        Options options = null;
        try
        {
            options = Options.Parse(args);
            if (options.InstallShortcuts || options.RestoreShortcuts)
            {
                WriteReport(options.ReportPath, new LaunchReport { Action = options.RestoreShortcuts ? Shortcuts.Restore() : Shortcuts.Install() });
                return 0;
            }
            string executable = FindApplication(options.ExecutablePath);
            if (!options.CheckOnly && IsApplicationRunning(executable))
            {
                // Electron forwards this second launch to the existing instance.
                using (Process process = Process.Start(CreateStartInfo(executable, options.ApplicationArguments, null))) { }
                WriteReport(options.ReportPath, new LaunchReport { Action = "opened-existing-instance", ExecutablePath = executable });
                return 0;
            }
            ProxyConfiguration proxy = ReadProxy(options);
            VerifyProxyPorts(proxy);
            var report = new LaunchReport {
                Action = options.CheckOnly ? "checks-passed" : "started",
                ExecutablePath = executable, ProxySource = proxy.Source,
                HttpProxy = proxy.HttpProxy, HttpsProxy = proxy.HttpsProxy, NoProxy = proxy.NoProxy
            };
            if (!options.CheckOnly)
            {
                using (Process process = Process.Start(CreateStartInfo(executable, options.ApplicationArguments, proxy)))
                {
                    report.ProcessId = process.Id;
                    // A second click may race startup and legitimately exit with code zero.
                    if (process.WaitForExit(StartupObservationMilliseconds) && process.ExitCode != 0)
                        throw new InvalidOperationException("Antigravity 启动失败，退出代码：" + process.ExitCode);
                }
            }
            WriteReport(options.ReportPath, report);
            return 0;
        }
        catch (Exception exception)
        {
            if (options != null && !String.IsNullOrEmpty(options.ReportPath))
            {
                try { WriteReport(options.ReportPath, new LaunchReport { Action = "failed", Error = exception.Message }); }
                catch (Exception) { }
            }
            else MessageBox.Show(exception.Message, "Antigravity", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    internal static string FindApplication(string suppliedPath)
    {
        if (!String.IsNullOrEmpty(suppliedPath))
        {
            string fullPath = Path.GetFullPath(suppliedPath);
            if (File.Exists(fullPath) && !IsLauncherPath(fullPath)) return fullPath;
            throw new FileNotFoundException("指定的 Antigravity 程序不存在。");
        }
        var candidates = new List<string> { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ApplicationFileName) };
        AddCandidate(candidates, Environment.SpecialFolder.LocalApplicationData, "Programs", "antigravity");
        AddCandidate(candidates, Environment.SpecialFolder.ProgramFiles, "Antigravity");
        AddCandidate(candidates, Environment.SpecialFolder.ProgramFilesX86, "Antigravity");
        foreach (string candidate in candidates)
            if (File.Exists(candidate) && !IsLauncherPath(candidate)) return Path.GetFullPath(candidate);
        foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (string keyPath in new[] {
                @"Software\Microsoft\Windows\CurrentVersion\App Paths\Antigravity.exe",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\Antigravity.exe" })
            {
                using (RegistryKey key = hive.OpenSubKey(keyPath))
                    if (key != null && key.GetValue(null) is string) candidates.Add((string)key.GetValue(null));
            }
            foreach (string keyPath in new[] {
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using (RegistryKey root = hive.OpenSubKey(keyPath))
                {
                    if (root == null) continue;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        using (RegistryKey key = root.OpenSubKey(name))
                        {
                            if (key == null || !String.Equals(key.GetValue("DisplayName") as string, "Antigravity", StringComparison.OrdinalIgnoreCase)) continue;
                            string location = key.GetValue("InstallLocation") as string;
                            if (!String.IsNullOrWhiteSpace(location)) candidates.Add(Path.Combine(location, ApplicationFileName));
                        }
                    }
                }
            }
        }
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(candidate) && !IsLauncherPath(candidate)) return Path.GetFullPath(candidate);
        throw new FileNotFoundException("未找到 Antigravity。请将启动器放到安装目录旁，或使用 --executable 指定程序路径。");
    }

    private static bool IsLauncherPath(string candidate)
    {
        using (Process current = Process.GetCurrentProcess())
            return String.Equals(Path.GetFullPath(candidate), current.MainModule.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCandidate(List<string> candidates, Environment.SpecialFolder folder, params string[] parts)
    {
        string root = Environment.GetFolderPath(folder);
        if (String.IsNullOrEmpty(root)) return;
        foreach (string part in parts) root = Path.Combine(root, part);
        candidates.Add(Path.Combine(root, ApplicationFileName));
    }

    private static bool IsApplicationRunning(string executable)
    {
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try { if (String.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase)) return true; }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
        return false;
    }

    private static ProxyConfiguration ReadProxy(Options options)
    {
        if (options.ProxyPort.HasValue)
            return ProxyConfiguration.FromServer("http://" + IPAddress.Loopback + ":" + options.ProxyPort.Value, "", "explicit port");
        if (!String.IsNullOrEmpty(options.ProxyUrl))
            return ProxyConfiguration.FromServer(options.ProxyUrl, "", "explicit URL");
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey))
        {
            if (key == null) return ProxyConfiguration.Direct();
            string pac = key.GetValue("AutoConfigURL") as string;
            if (!String.IsNullOrWhiteSpace(pac))
                throw new InvalidOperationException("当前配置使用 PAC，尚不支持。可使用 --proxy-port 指定本地 HTTP/mixed 代理端口。");
            bool enabled = Convert.ToInt32(key.GetValue("ProxyEnable", 0), CultureInfo.InvariantCulture) != 0;
            if (!enabled) return ProxyConfiguration.Direct();
            string server = key.GetValue("ProxyServer") as string;
            if (String.IsNullOrWhiteSpace(server)) throw new InvalidOperationException("系统代理已开启，但代理地址为空。");
            return ProxyConfiguration.FromServer(server, key.GetValue("ProxyOverride", "") as string, "Windows system proxy");
        }
    }

    internal static void VerifyProxyPorts(ProxyConfiguration configuration)
    {
        foreach (string proxy in new[] { configuration.HttpProxy, configuration.HttpsProxy }.Where(value => value != null).Distinct())
        {
            Uri uri = new Uri(proxy);
            using (var client = new TcpClient())
            {
                try
                {
                    var task = client.ConnectAsync(uri.DnsSafeHost, uri.Port);
                    if (!task.Wait(ProxyConnectTimeoutMilliseconds)) throw new TimeoutException();
                    task.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException("无法连接代理 " + uri.Authority + "，请确认代理软件已启动。", exception);
                }
            }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> forwardedArguments, ProxyConfiguration configuration)
    {
        var arguments = new List<string>();
        if (configuration != null)
        {
            if (configuration.HttpProxy == null) arguments.Add("--no-proxy-server");
            else
            {
                arguments.Add("--proxy-server=http=" + configuration.HttpProxy + ";https=" + configuration.HttpsProxy);
                string bypass = "localhost;127.0.0.1;[::1]";
                if (!String.IsNullOrEmpty(configuration.Bypass)) bypass += ";" + configuration.Bypass;
                arguments.Add("--proxy-bypass-list=" + bypass);
            }
        }
        arguments.AddRange(forwardedArguments);
        var info = new ProcessStartInfo {
            FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable),
            Arguments = String.Join(" ", arguments.Select(QuoteArgument)),
            UseShellExecute = false, CreateNoWindow = true
        };
        if (configuration != null)
        {
            // Only mutate the child environment; Windows names may use mixed case.
            var keys = new HashSet<string>(new[] {
                "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "NODE_USE_ENV_PROXY",
                "ELECTRON_RUN_AS_NODE", "ELECTRON_OZONE_PLATFORM_HINT", "DEV_URL"
            }, StringComparer.OrdinalIgnoreCase);
            foreach (string key in info.EnvironmentVariables.Keys.Cast<string>().ToArray())
                if (keys.Contains(key)) info.EnvironmentVariables.Remove(key);
            if (configuration.HttpProxy != null)
            {
                info.EnvironmentVariables["HTTP_PROXY"] = configuration.HttpProxy;
                info.EnvironmentVariables["HTTPS_PROXY"] = configuration.HttpsProxy;
                info.EnvironmentVariables["NO_PROXY"] = configuration.NoProxy;
                info.EnvironmentVariables["NODE_USE_ENV_PROXY"] = "1";
            }
        }
        return info;
    }

    internal static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static void WriteReport(string path, LaunchReport report)
    {
        if (String.IsNullOrEmpty(path)) return; // Normal use creates no sidecar files.
        report.Time = DateTimeOffset.Now.ToString("o");
        JsonFiles.Write(Path.GetFullPath(path), report);
    }
}

internal sealed class Options
{
    internal bool CheckOnly, InstallShortcuts, RestoreShortcuts;
    internal int? ProxyPort;
    internal string ProxyUrl, ExecutablePath, ReportPath;
    internal readonly List<string> ApplicationArguments = new List<string>();
    internal static Options Parse(string[] args)
    {
        var result = new Options();
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (option == "--") { result.ApplicationArguments.AddRange(args.Skip(index + 1)); break; }
            switch (option)
            {
                case "--check": result.CheckOnly = true; break;
                case "--install-shortcuts": result.InstallShortcuts = true; break;
                case "--restore-shortcuts": result.RestoreShortcuts = true; break;
                case "--proxy-port":
                    int port;
                    if (++index >= args.Length || !Int32.TryParse(args[index], out port) || port < 1 || port > UInt16.MaxValue)
                        throw new ArgumentException("--proxy-port 需要有效端口号。");
                    result.ProxyPort = port;
                    break;
                case "--proxy-url": result.ProxyUrl = Next(args, ref index); break;
                case "--executable": result.ExecutablePath = Next(args, ref index); break;
                case "--report": result.ReportPath = Next(args, ref index); break;
                default: throw new ArgumentException("未知启动器参数。应用参数请放在 -- 后面。");
            }
        }
        if (result.ProxyPort.HasValue && result.ProxyUrl != null) throw new ArgumentException("不能同时指定 --proxy-port 和 --proxy-url。");
        if ((result.CheckOnly ? 1 : 0) + (result.InstallShortcuts ? 1 : 0) + (result.RestoreShortcuts ? 1 : 0) > 1)
            throw new ArgumentException("检查、安装快捷方式、恢复快捷方式不能同时使用。");
        return result;
    }
    private static string Next(string[] args, ref int index)
    {
        if (++index >= args.Length || String.IsNullOrEmpty(args[index])) throw new ArgumentException("参数缺少值。");
        return args[index];
    }
}

internal sealed class ProxyConfiguration
{
    internal string HttpProxy, HttpsProxy, Bypass, NoProxy, Source;
    internal static ProxyConfiguration Direct() { return new ProxyConfiguration { Source = "Windows system proxy disabled (direct connection)" }; }
    internal static ProxyConfiguration FromServer(string server, string bypass, string source)
    {
        bypass = bypass ?? "";
        var hosts = new List<string> { "localhost", "127.0.0.1", "::1" };
        foreach (string entry in bypass.Split(';'))
        {
            string host = entry.Trim();
            if (host.StartsWith("*.")) host = host.Substring(1);
            if (Regex.IsMatch(host, @"^\.?[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)*$")) hosts.Add(host);
        }
        return new ProxyConfiguration {
            HttpProxy = Resolve(server, "http"), HttpsProxy = Resolve(server, "https"),
            Bypass = bypass, NoProxy = String.Join(",", hosts.Distinct(StringComparer.OrdinalIgnoreCase)), Source = source
        };
    }
    private static string Resolve(string server, string scheme)
    {
        string entry = server.Trim();
        if ((!entry.Contains("://") && entry.Contains("=")) || Regex.IsMatch(entry, @"^\s*(https?|socks)\s*=", RegexOptions.IgnoreCase))
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in entry.Split(';'))
            {
                int separator = part.IndexOf('=');
                if (separator > 0) map[part.Substring(0, separator).Trim()] = part.Substring(separator + 1).Trim();
            }
            if (!map.TryGetValue(scheme, out entry) && !map.TryGetValue("http", out entry))
                throw new ArgumentException("系统代理缺少 HTTP 代理地址。请使用 HTTP/mixed 端口。");
        }
        if (!entry.Contains("://")) entry = "http://" + entry;
        Uri uri;
        if (!Uri.TryCreate(entry, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttp || String.IsNullOrEmpty(uri.Host) ||
            uri.Port < 1 || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("需要不含账号密码或路径的 HTTP 代理地址。");
        return uri.GetLeftPart(UriPartial.Authority);
    }
}

[DataContract]
internal sealed class LaunchReport
{
    [DataMember(Name = "time")] public string Time;
    [DataMember(Name = "action")] public string Action;
    [DataMember(Name = "executablePath", EmitDefaultValue = false)] public string ExecutablePath;
    [DataMember(Name = "proxySource", EmitDefaultValue = false)] public string ProxySource;
    [DataMember(Name = "httpProxy", EmitDefaultValue = false)] public string HttpProxy;
    [DataMember(Name = "httpsProxy", EmitDefaultValue = false)] public string HttpsProxy;
    [DataMember(Name = "noProxy", EmitDefaultValue = false)] public string NoProxy;
    [DataMember(Name = "processId", EmitDefaultValue = false)] public int ProcessId;
    [DataMember(Name = "error", EmitDefaultValue = false)] public string Error;
}

internal static class JsonFiles
{
    internal static T Read<T>(string path)
    {
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path))))
            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
    }
    internal static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
    }
}

[DataContract]
internal sealed class ShortcutManifest
{
    [DataMember(Name = "createdAt")] public string CreatedAt;
    [DataMember(Name = "launcherPath")] public string LauncherPath;
    [DataMember(Name = "shortcuts")] public List<ShortcutRecord> Shortcuts;
}
[DataContract]
internal sealed class ShortcutRecord
{
    [DataMember(Name = "path")] public string Path;
    [DataMember(Name = "originalTarget")] public string OriginalTarget;
    [DataMember(Name = "originalArguments")] public string OriginalArguments;
    [DataMember(Name = "originalIcon")] public string OriginalIcon;
    [DataMember(Name = "originalWorkingDirectory")] public string OriginalWorkingDirectory;
    [DataMember(Name = "originalAppId")] public string OriginalAppId;
    [DataMember(Name = "originalSha256")] public string OriginalSha256;
    [DataMember(Name = "backupPath")] public string BackupPath;
}

internal static class Shortcuts
{
    private static readonly string LauncherPath = Process.GetCurrentProcess().MainModule.FileName;
    private static readonly string IntegrationDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "integration");
    private static readonly string ManifestPath = Path.Combine(IntegrationDirectory, "shortcuts.json");
    private static List<string> AllowedPaths()
    {
        var paths = new List<string>();
        foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs })
        {
            string root = Environment.GetFolderPath(folder);
            if (!String.IsNullOrEmpty(root)) paths.Add(Path.Combine(root, "Antigravity.lnk"));
        }
        string pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned");
        if (Directory.Exists(pinned)) paths.AddRange(Directory.GetFiles(pinned, "Antigravity.lnk", SearchOption.AllDirectories));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    private static string AppId(string path)
    {
        dynamic explorer = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
        dynamic folder = null, item = null;
        try
        {
            folder = explorer.NameSpace(Path.GetDirectoryName(path));
            if (folder == null) return "";
            item = folder.ParseName(Path.GetFileName(path));
            return item == null ? "" : Convert.ToString(item.ExtendedProperty("System.AppUserModel.ID"));
        }
        finally { Release(item); Release(folder); Release(explorer); }
    }
    private static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
    private static void ValidateBackup(ShortcutRecord record)
    {
        if (!AllowedPaths().Contains(record.Path, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("快捷方式位置与当前用户不匹配。");
        string backup = Path.GetFullPath(record.BackupPath);
        string root = Path.GetFullPath(Path.Combine(IntegrationDirectory, "backups")) + Path.DirectorySeparatorChar;
        if (!backup.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(backup) || Hash(backup) != record.OriginalSha256)
            throw new InvalidOperationException("快捷方式备份校验失败，未修改快捷方式。");
    }
    private static void SaveBackup(ShortcutRecord record)
    {
        string root = Path.Combine(IntegrationDirectory, "backups");
        Directory.CreateDirectory(root);
        record.BackupPath = Path.Combine(root, record.OriginalSha256 + "-original.lnk");
        if (!File.Exists(record.BackupPath)) File.Copy(record.Path, record.BackupPath, false);
        ValidateBackup(record);
    }
    private static string InstalledIcon(ShortcutRecord record, string application)
    {
        // An implicit icon must continue to show the application's icon after retargeting.
        return String.IsNullOrEmpty(record.OriginalIcon) || record.OriginalIcon.StartsWith(",")
            ? application + ",0" : record.OriginalIcon;
    }

    internal static string Install()
    {
        string application = DesktopLauncher.FindApplication(null);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        var records = new List<ShortcutRecord>();
        try
        {
            foreach (string path in AllowedPaths().Where(File.Exists))
            {
                dynamic link = shell.CreateShortcut(path);
                try
                {
                    string target = link.TargetPath;
                    if (!String.Equals(target, application, StringComparison.OrdinalIgnoreCase) && !String.Equals(target, LauncherPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!String.IsNullOrEmpty((string)link.Arguments)) throw new InvalidOperationException("快捷方式包含自定义参数，未修改。");
                    records.Add(new ShortcutRecord {
                        Path = path, OriginalTarget = target, OriginalArguments = link.Arguments,
                        OriginalIcon = link.IconLocation, OriginalWorkingDirectory = link.WorkingDirectory,
                        OriginalAppId = AppId(path), OriginalSha256 = Hash(path)
                    });
                }
                finally { Release(link); }
            }
            if (records.Count == 0) throw new InvalidOperationException("未找到当前用户的 Antigravity 快捷方式。");
            ShortcutManifest manifest;
            if (File.Exists(ManifestPath))
            {
                manifest = JsonFiles.Read<ShortcutManifest>(ManifestPath);
                foreach (ShortcutRecord record in records)
                {
                    ShortcutRecord original = manifest.Shortcuts.SingleOrDefault(value => String.Equals(value.Path, record.Path, StringComparison.OrdinalIgnoreCase));
                    if (original == null) throw new InvalidOperationException("快捷方式列表已变化，请先恢复原入口。");
                    ValidateBackup(original);
                    if (!String.Equals(record.OriginalTarget, LauncherPath, StringComparison.OrdinalIgnoreCase) && record.OriginalSha256 != original.OriginalSha256)
                    {
                        // A rebuilt application shortcut is a new baseline; retain both versions.
                        SaveBackup(record);
                        manifest.Shortcuts.Remove(original);
                        manifest.Shortcuts.Add(record);
                    }
                }
                manifest.LauncherPath = LauncherPath;
                JsonFiles.Write(ManifestPath, manifest);
            }
            else
            {
                if (records.Any(record => String.Equals(record.OriginalTarget, LauncherPath, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("快捷方式已接入，但原始备份缺失。");
                foreach (ShortcutRecord record in records) SaveBackup(record);
                manifest = new ShortcutManifest { CreatedAt = DateTimeOffset.Now.ToString("o"), LauncherPath = LauncherPath, Shortcuts = records };
                JsonFiles.Write(ManifestPath, manifest);
            }
            var changed = new List<string>();
            try
            {
                foreach (ShortcutRecord record in records)
                {
                    changed.Add(record.Path);
                    dynamic link = shell.CreateShortcut(record.Path);
                    try
                    {
                        link.TargetPath = LauncherPath;
                        link.WorkingDirectory = record.OriginalWorkingDirectory;
                        link.IconLocation = InstalledIcon(record, application);
                        link.Save();
                    }
                    finally { Release(link); }
                    dynamic check = shell.CreateShortcut(record.Path);
                    try
                    {
                        if (!String.Equals((string)check.TargetPath, LauncherPath, StringComparison.OrdinalIgnoreCase) ||
                            (string)check.IconLocation != InstalledIcon(record, application) || (string)check.WorkingDirectory != record.OriginalWorkingDirectory || AppId(record.Path) != record.OriginalAppId)
                            throw new InvalidOperationException("快捷方式校验失败，正在恢复原入口。");
                    }
                    finally { Release(check); }
                }
            }
            catch
            {
                foreach (string path in changed)
                {
                    ShortcutRecord original = manifest.Shortcuts.Single(record => String.Equals(record.Path, path, StringComparison.OrdinalIgnoreCase));
                    File.Copy(original.BackupPath, original.Path, true);
                }
                throw;
            }
            return "shortcuts-installed";
        }
        finally { Release(shell); }
    }

    internal static string Restore()
    {
        if (!File.Exists(ManifestPath)) throw new FileNotFoundException("未找到快捷方式备份。");
        ShortcutManifest manifest = JsonFiles.Read<ShortcutManifest>(ManifestPath);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        try
        {
            foreach (ShortcutRecord record in manifest.Shortcuts)
            {
                ValidateBackup(record);
                if (!File.Exists(record.Path)) continue;
                dynamic link = shell.CreateShortcut(record.Path);
                try
                {
                    string target = link.TargetPath;
                    if (!String.Equals(target, manifest.LauncherPath, StringComparison.OrdinalIgnoreCase) && !String.Equals(target, record.OriginalTarget, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("快捷方式被其他操作修改，未覆盖该变更。");
                }
                finally { Release(link); }
            }
            foreach (ShortcutRecord record in manifest.Shortcuts)
            {
                File.Copy(record.BackupPath, record.Path, true);
                if (Hash(record.Path) != record.OriginalSha256) throw new IOException("快捷方式恢复校验失败。");
            }
            return "shortcuts-restored";
        }
        finally { Release(shell); }
    }
}
