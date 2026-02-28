using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RxTool
{
    public static class WifiHelper
    {
        public static async Task<string?> WaitAndConnectAsync(WifiMatch match, string password, TimeSpan timeout, Action<string> log)
        {
            var sw = Stopwatch.StartNew();

            while (sw.Elapsed < timeout)
            {
                var visible = GetVisibleSsids();
                var ssid = PickSsid(visible, match);

                if (!string.IsNullOrWhiteSpace(ssid))
                {
                    log($"SSID найден: {ssid}. Пытаюсь подключиться...");

                    EnsureProfile(ssid, password, log);
                    Connect(ssid, log);

                    await Task.Delay(2500);

                    if (IsConnectedToSsid(ssid))
                    {
                        log($"Подключено к SSID: {ssid}");
                        return ssid;
                    }

                    log("Пока не подключилось, повторяю...");
                }
                else
                {
                    log($"Жду SSID ({match.Mode}): {match.Value} ...");
                }

                await Task.Delay(1500);
            }

            return null;
        }

        public static bool IsConnectedToSsid(string ssid)
        {
            var output = RunNetsh("wlan show interfaces");
            return Regex.IsMatch(output, @"^\s*SSID\s*:\s*" + Regex.Escape(ssid) + @"\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }

        public static List<string> GetVisibleSsids()
        {
            var output = RunNetsh("wlan show networks mode=bssid");
            // Ищем строки: SSID N : <name>
            var list = new List<string>();
            foreach (Match m in Regex.Matches(output, @"^\s*SSID\s+\d+\s*:\s*(.*)\s*$",
                         RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                var name = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(name) && !list.Contains(name))
                    list.Add(name);
            }
            return list;
        }

        public static string? PickSsid(List<string> visible, WifiMatch match)
        {
            var mode = (match.Mode ?? "exact").Trim().ToLowerInvariant();
            var val = (match.Value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(val)) return null;

            if (mode == "exact")
                return visible.Find(s => string.Equals(s, val, StringComparison.OrdinalIgnoreCase));

            if (mode == "startswith")
                return visible.Find(s => s.StartsWith(val, StringComparison.OrdinalIgnoreCase));

            if (mode == "regex")
            {
                var re = new Regex(val, RegexOptions.IgnoreCase);
                return visible.Find(s => re.IsMatch(s));
            }

            // fallback
            return visible.Find(s => string.Equals(s, val, StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureProfile(string ssid, string password, Action<string> log)
        {
            var profiles = RunNetsh("wlan show profiles");

            // Чтобы пароль точно был актуальный — пересоздаём профиль
            if (profiles.IndexOf(ssid, StringComparison.OrdinalIgnoreCase) >= 0)
                RunNetsh($"wlan delete profile name=\"{ssid}\"");

            var xml = BuildWlanProfileXml(ssid, password);
            var tmp = Path.Combine(Path.GetTempPath(), $"rx_{Guid.NewGuid():N}.xml");
            File.WriteAllText(tmp, xml, Encoding.UTF8);

            var res = RunNetsh($"wlan add profile filename=\"{tmp}\" user=current");
            log("add profile: " + FirstLine(res));

            try { File.Delete(tmp); } catch { }
        }

        private static void Connect(string ssid, Action<string> log)
        {
            var res = RunNetsh($"wlan connect name=\"{ssid}\" ssid=\"{ssid}\"");
            log("connect: " + FirstLine(res));
        }

        private static string BuildWlanProfileXml(string ssid, string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
  <name>{SecurityElement.Escape(ssid)}</name>
  <SSIDConfig><SSID><name>{SecurityElement.Escape(ssid)}</name></SSID></SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>auto</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>open</authentication>
        <encryption>none</encryption>
        <useOneX>false</useOneX>
      </authEncryption>
    </security>
  </MSM>
</WLANProfile>";
            }

            return $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
  <name>{SecurityElement.Escape(ssid)}</name>
  <SSIDConfig><SSID><name>{SecurityElement.Escape(ssid)}</name></SSID></SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>auto</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>WPA2PSK</authentication>
        <encryption>AES</encryption>
        <useOneX>false</useOneX>
      </authEncryption>
      <sharedKey>
        <keyType>passPhrase</keyType>
        <protected>false</protected>
        <keyMaterial>{SecurityElement.Escape(password)}</keyMaterial>
      </sharedKey>
    </security>
  </MSM>
</WLANProfile>";
        }

        private static string RunNetsh(string args)
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();

            return string.IsNullOrWhiteSpace(err) ? output : (output + "\n" + err);
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Replace("\r", "");
            var idx = s.IndexOf('\n');
            return idx >= 0 ? s.Substring(0, idx).Trim() : s.Trim();
        }
    }
}