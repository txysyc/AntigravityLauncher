// SPDX-License-Identifier: MIT
// Copyright (c) 2026 txyc

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;

internal static class LauncherTests
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (ArgumentException) { assertions++; return; }
        throw new Exception(message);
    }

    private static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream));
    }

    private static LaunchReport SetIcon(string launcher, string source, string report)
    {
        using (Process process = Process.Start(new ProcessStartInfo {
            FileName = launcher, UseShellExecute = false, CreateNoWindow = true,
            Arguments = "--use-app-icon --executable " + DesktopLauncher.QuoteArgument(source) +
                " --report " + DesktopLauncher.QuoteArgument(report)
        }))
        {
            if (!process.WaitForExit(15000)) throw new Exception("Icon command did not exit");
        }
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 30000)
        {
            if (File.Exists(report))
            {
                LaunchReport result = null;
                try { result = JsonFiles.Read<LaunchReport>(report); }
                catch (IOException) { }
                catch (SerializationException) { }
                if (result != null)
                {
                    if (result.ProcessId > 0)
                    {
                        Process worker = null;
                        try { worker = Process.GetProcessById(result.ProcessId); }
                        catch (ArgumentException) { }
                        if (worker != null)
                            using (worker)
                                if (!worker.WaitForExit(15000)) throw new Exception("Icon worker did not exit");
                    }
                    return result;
                }
            }
            Thread.Sleep(100);
        }
        throw new Exception("Icon result was not written");
    }

    private static void TestIconCustomization()
    {
        string original = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "dist", "AntigravityLauncher.exe"));
        if (!File.Exists(original)) throw new Exception("Build dist/AntigravityLauncher.exe before running tests");
        string directory = Path.Combine(Path.GetTempPath(), "Antigravity 图标测试 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string launcher = Path.Combine(directory, "启动器 test.exe");
        string failedReport = Path.Combine(directory, "failed.json");
        string successReport = Path.Combine(directory, "success.json");
        string source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        string originalHash = Hash(original);
        string sourceHash = Hash(source);
        File.Copy(original, launcher);
        try
        {
            // 默认发行版不含图标，图标缺失时应保持原文件且清理临时副本。
            LaunchReport failed = SetIcon(launcher, original, failedReport);
            Check(failed.Action == "failed", "Iconless source was accepted");
            Check(Hash(launcher) == originalHash, "Failed icon update changed launcher");
            Check(Directory.GetDirectories(directory).Length == 0, "Failed icon update left temporary files");

            LaunchReport success = SetIcon(launcher, source, successReport);
            Check(success.Action == "icon-updated", "Icon update failed: " + success.Error);
            Check(IconResources.Fingerprint(launcher) == IconResources.Fingerprint(source), "Copied icon resources differ");
            Check(Hash(launcher) != originalHash, "Icon update did not change the executable");
            Check(Hash(original) == originalHash && Hash(source) == sourceHash, "Icon update modified its sources");
            Check(Directory.GetDirectories(directory).Length == 0, "Icon workers were not cleaned up");
            Check(FileVersionInfo.GetVersionInfo(launcher).ProductVersion == "0.1.0", "Icon update lost version metadata");
            Assembly assembly = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(launcher));
            foreach (string name in new[] { "AntigravityProxyLauncher.LICENSE", "AntigravityProxyLauncher.THIRD_PARTY_NOTICES.md" })
            {
                using (Stream stream = assembly.GetManifestResourceStream(name))
                    Check(stream != null && stream.Length > 0, "Icon update lost embedded notice: " + name);
            }
        }
        finally
        {
            // 只删除测试自身创建的已知文件，异常残留保留供排查。
            foreach (string path in new[] { launcher, failedReport, successReport })
                if (File.Exists(path)) File.Delete(path);
            if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory, false);
        }
    }
    private static int Main()
    {
        try
        {
            var proxy = ProxyConfiguration.FromServer("http=127.0.0.1:8101;https=127.0.0.1:8102", "<local>;*.example.test;dev.example.test;*.example.test", "test");
            Check(proxy.HttpProxy == "http://127.0.0.1:8101" && proxy.HttpsProxy == "http://127.0.0.1:8102", "Per-scheme proxy parsing failed");
            Check(proxy.NoProxy == "localhost,127.0.0.1,::1,.example.test,dev.example.test", "Loopback and bypass conversion failed");
            var fallback = ProxyConfiguration.FromServer("http=localhost:8101", "", "test");
            Check(fallback.HttpProxy == fallback.HttpsProxy, "HTTP fallback failed");
            string ipv6Proxy = ProxyConfiguration.FromServer("http://[::1]:8101", "", "test").HttpProxy;
            var ipv6Uri = new Uri(ipv6Proxy);
            Check(IPAddress.Parse(ipv6Uri.DnsSafeHost).Equals(IPAddress.IPv6Loopback) && ipv6Uri.Port == 8101, "IPv6 proxy parsing failed: " + ipv6Proxy);
            foreach (string value in new[] { "socks=localhost:8101", "socks5://localhost:8101", "http://user:secret@localhost:8101", "http://localhost:8101/path", "http://localhost:8101/?key=value", "http://localhost:8101/#fragment" })
                Reject(() => ProxyConfiguration.FromServer(value, "", "test"), "Invalid proxy accepted");
            Reject(() => Options.Parse(new[] { "--proxy-port", "0" }), "Invalid port accepted");
            Reject(() => Options.Parse(new[] { "--proxy-port", "8101", "--proxy-url", "http://localhost:8101" }), "Conflicting proxies accepted");
            Reject(() => Options.Parse(new[] { "--install-shortcuts", "--restore-shortcuts" }), "Conflicting modes accepted");
            Check(Options.Parse(new[] { "--use-app-icon" }).UseApplicationIcon, "Icon option parsing failed");
            Reject(() => Options.Parse(new[] { "--use-app-icon", "--check" }), "Icon and check modes accepted");
            Reject(() => Options.Parse(new[] { "--use-app-icon", "--proxy-port", "8101" }), "Icon mode accepted proxy options");
            Reject(() => Options.Parse(new[] { "--use-app-icon", "--", "--help" }), "Icon mode accepted app arguments");
            Check(Options.Parse(new[] { "--", "--user-data-dir", "folder with spaces" }).ApplicationArguments.Count == 2, "Argument forwarding failed");

            string previous = Environment.GetEnvironmentVariable("HTTP_PROXY");
            string previousAll = Environment.GetEnvironmentVariable("ALL_PROXY");
            try
            {
                Environment.SetEnvironmentVariable("HTTP_PROXY", "http://stale.invalid:8103");
                Environment.SetEnvironmentVariable("ALL_PROXY", "socks5://stale.invalid:8103");
                var info = DesktopLauncher.CreateStartInfo(Path.Combine(Environment.CurrentDirectory, "sample.exe"), new string[0], proxy);
                Check(info.EnvironmentVariables["HTTP_PROXY"] == proxy.HttpProxy && info.EnvironmentVariables["HTTPS_PROXY"] == proxy.HttpsProxy, "Child proxy environment failed");
                Check(!info.EnvironmentVariables.ContainsKey("ALL_PROXY"), "Conflicting proxy leaked to child");
                Check(Environment.GetEnvironmentVariable("HTTP_PROXY") == "http://stale.invalid:8103", "Parent environment was modified");
                Check(info.Arguments.Contains("--proxy-server=http="), "Electron proxy argument missing");
                var direct = DesktopLauncher.CreateStartInfo(Path.Combine(Environment.CurrentDirectory, "sample.exe"), new string[0], ProxyConfiguration.Direct());
                Check(!direct.EnvironmentVariables.ContainsKey("HTTP_PROXY") && !direct.EnvironmentVariables.ContainsKey("ALL_PROXY"), "Direct mode inherited stale proxy");
                Check(direct.Arguments.Contains("--no-proxy-server"), "Direct Electron mode missing");
            }
            finally { Environment.SetEnvironmentVariable("HTTP_PROXY", previous); Environment.SetEnvironmentVariable("ALL_PROXY", previousAll); }

            string[] values = { "", "plain", "space value", "中文目录\\", "quoted\"value", "backslash\\\"quote", "two trailing\\\\" };
            string line = "test.exe " + String.Join(" ", values.Select(DesktopLauncher.QuoteArgument));
            int count;
            IntPtr pointer = CommandLineToArgvW(line, out count);
            if (pointer == IntPtr.Zero) throw new Exception("Windows command line parser failed");
            try
            {
                Check(count == values.Length + 1, "Windows argv count mismatch");
                for (int index = 0; index < values.Length; index++)
                    Check(Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, (index + 1) * IntPtr.Size)) == values[index], "Windows argv round-trip failed");
            }
            finally { LocalFree(pointer); }

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int listeningPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            try { DesktopLauncher.VerifyProxyPorts(ProxyConfiguration.FromServer("http://127.0.0.1:" + listeningPort, "", "test")); assertions++; }
            finally { listener.Stop(); }
            bool refused = false;
            try { DesktopLauncher.VerifyProxyPorts(ProxyConfiguration.FromServer("http://127.0.0.1:" + listeningPort, "", "test")); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused, "Closed proxy port accepted");
            DesktopLauncher.VerifyProxyPorts(ProxyConfiguration.Direct());
            TestIconCustomization();
            Console.WriteLine("Passed: " + assertions + " assertions (proxy parsing, private environment, Windows argv, TCP preflight, local icon customization).");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
