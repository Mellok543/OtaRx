using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ElrsWifiBatchFlasher.Services;

public sealed class NetshWifiService
{
    private static async Task<(int exit, string stdout, string stderr)> Run(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi };
        p.Start();
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, stdout, stderr);
    }

    public async Task<string?> GetConnectedSsidAsync()
    {
        var (_, outp, _) = await Run("wlan show interfaces");
        // English/Russian Windows differ; try robust regex:
        // "SSID                   : MyWifi"
        // "SSID                   : <not connected>"
        var m = Regex.Match(outp, @"^\s*SSID\s*:\s*(.+)\s*$", RegexOptions.Multiline);
        if (!m.Success) return null;
        var ssid = m.Groups[1].Value.Trim();
        if (ssid.Contains("not connected", StringComparison.OrdinalIgnoreCase)) return null;
        if (ssid.Contains("не подключ", StringComparison.OrdinalIgnoreCase)) return null;
        if (ssid == "") return null;
        return ssid;
    }

    public async Task<List<string>> ListVisibleSsidsAsync()
    {
        var (_, outp, _) = await Run("wlan show networks mode=bssid");
        // lines like: "SSID 1 : ExpressLRS RX"
        var ssids = Regex.Matches(outp, @"^\s*SSID\s+\d+\s*:\s*(.*)\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return ssids;
    }

    public async Task EnsureProfileAsync(string ssid, string? password, Action<string>? log = null)
    {
        // 1) Сформировать XML профиля
        var xml = BuildProfileXml(ssid, password);

        // 2) netsh часто лучше ест UTF-16LE (Unicode) XML
        var tmp = Path.Combine(Path.GetTempPath(), $"elrs_{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(tmp, xml, Encoding.Unicode);

        try
        {
            // 3) На всякий случай удалим профиль (если существовал)
            await Run($@"wlan delete profile name=""{ssid}""");

            // 4) Пробуем добавить профиль для текущего пользователя
            var (code1, out1, err1) = await Run($@"wlan add profile filename=""{tmp}"" user=current");
            log?.Invoke($"netsh add profile (current) exit={code1}\n{out1}\n{err1}");

            if (code1 == 0) return;

            // 5) Фоллбэк: пробуем user=all (иногда current запрещён политиками)
            var (code2, out2, err2) = await Run($@"wlan add profile filename=""{tmp}"" user=all");
            log?.Invoke($"netsh add profile (all) exit={code2}\n{out2}\n{err2}");

            if (code2 != 0)
            {
                throw new InvalidOperationException(
                    "netsh add profile failed.\n" +
                    $"(current) exit={code1}\n{out1}\n{err1}\n" +
                    $"(all) exit={code2}\n{out2}\n{err2}");
            }
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    public async Task ConnectAsync(string ssid, int timeoutMs, CancellationToken ct)
    {
        var start = Environment.TickCount64;

        // request connect
        await Run($@"wlan connect name=""{ssid}"" ssid=""{ssid}""");

        while (!ct.IsCancellationRequested && Environment.TickCount64 - start < timeoutMs)
        {
            var cur = await GetConnectedSsidAsync();
            if (string.Equals(cur, ssid, StringComparison.Ordinal))
                return;

            await Task.Delay(500, ct);
        }

        throw new TimeoutException($"Wi-Fi connect timeout: {ssid}");
    }

    private static string BuildProfileXml(string ssid, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
  <name>{Esc(ssid)}</name>
  <SSIDConfig>
    <SSID>
      <name>{Esc(ssid)}</name>
    </SSID>
  </SSIDConfig>
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
  <name>{Esc(ssid)}</name>
  <SSIDConfig>
    <SSID>
      <name>{Esc(ssid)}</name>
    </SSID>
  </SSIDConfig>
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
        <keyMaterial>{Esc(password)}</keyMaterial>
      </sharedKey>
    </security>
  </MSM>
</WLANProfile>";
    }

    private static string Esc(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");
}