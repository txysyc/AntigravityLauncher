using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

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
            Console.WriteLine("Passed: " + assertions + " assertions (proxy parsing, private environment, Windows argv, TCP preflight).");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
