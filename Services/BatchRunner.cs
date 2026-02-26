using System.IO;

namespace ElrsWifiBatchFlasher.Services;

public sealed class BatchRunner
{
    private readonly NetshWifiService _wifi;

    public BatchRunner(NetshWifiService wifi)
    {
        _wifi = wifi;
    }

    public async Task RunAsync(
        BatchConfig cfg,
        Action<string> log,
        Action<int> setOkCount,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.FirmwarePath) || !File.Exists(cfg.FirmwarePath))
            throw new FileNotFoundException("Firmware .bin not found", cfg.FirmwarePath);

        var originalSsid = await _wifi.GetConnectedSsidAsync();
        log($"Current Wi-Fi: {(originalSsid ?? "(none)")}");
        log("Batch started. Put RX into WiFi mode one by one.");

        int ok = 0;

        while (!ct.IsCancellationRequested)
        {
            // 1) Find RX SSID (exact or prefix)
            var ssids = await _wifi.ListVisibleSsidsAsync();
            string? rxSsid = null;

            if (cfg.SsidIsPrefix)
                rxSsid = ssids.FirstOrDefault(s => s.StartsWith(cfg.RxSsid, StringComparison.Ordinal));
            else
                rxSsid = ssids.FirstOrDefault(s => string.Equals(s, cfg.RxSsid, StringComparison.Ordinal));

            if (rxSsid is null)
            {
                log($"RX AP not found. Waiting... (looking for {(cfg.SsidIsPrefix ? "prefix" : "SSID")} '{cfg.RxSsid}')");
                await Task.Delay(1000, ct);
                continue;
            }

            log($"Found RX AP: {rxSsid}");

            // 2) Ensure profile + connect
            await _wifi.EnsureProfileAsync(rxSsid, cfg.RxPassword, log);
            log($"Connecting to {rxSsid}...");
            await _wifi.ConnectAsync(rxSsid, cfg.ConnectTimeoutMs, ct);
            log("Connected. Waiting for 10.0.0.1...");

            // 3) OTA upload
            var ota = new ElrsOtaService(cfg.HttpTimeoutMs);
            if (!await ota.WaitWebAsync(ct))
                throw new TimeoutException("10.0.0.1 did not respond in time");

            var uploadPath = await ota.DetectUploadFormAsync(ct);
            log($"Uploading to {uploadPath} ...");

            var resp = await ota.UploadFirmwareAsync(cfg.FirmwarePath, ct);
            log($"Upload response: {Truncate(resp, 180)}");

            ok++;
            setOkCount(ok);

            log("Waiting RX reboot...");
            await Task.Delay(cfg.AfterUploadWaitMs, ct);

            // 4) Optionally reconnect back to original Wi-Fi each time
            // (Often not needed; leaving it on RX AP is OK until next AP appears.)
            if (!string.IsNullOrWhiteSpace(originalSsid))
            {
                log($"Reconnecting back to {originalSsid}...");
                try
                {
                    await _wifi.ConnectAsync(originalSsid, 10_000, ct);
                }
                catch
                {
                    log("Could not reconnect to original Wi-Fi (ignored).");
                }
            }

            log($"DONE #{ok}. Ready for next RX.");
            log("--------------------------------------------------");
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max) + "...";
}