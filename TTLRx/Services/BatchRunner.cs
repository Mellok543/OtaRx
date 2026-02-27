using System.IO;
using ElrsTtlBatchFlasher.Models;

namespace ElrsTtlBatchFlasher.Services;

public sealed class BatchRunner
{
    private readonly FlashConfig _cfg;
    private readonly Action<string> _log;
    private readonly Action<int> _setOk;
    private readonly Action<double> _setProgress;
    private readonly Action<string, bool, bool> _setStatus;

    public BatchRunner(
        FlashConfig cfg,
        Action<string> log,
        Action<int> setOk,
        Action<double> setProgress,
        Action<string, bool, bool> setStatus)
    {
        _cfg = cfg;
        _log = log;
        _setOk = setOk;
        _setProgress = setProgress;
        _setStatus = setStatus;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        ValidateFiles();

        var esptool = new EsptoolService(_cfg);
        int ok = 0;

        _log("Batch started. Put RX into BOOT and keep BOOT pressed until flashing starts.");
        _log("--------------------------------------------------");

        while (!ct.IsCancellationRequested)
        {
            _setStatus("Waiting for bootloader...", false, false);
            _setProgress(5);

            var detected = await WaitForBootAsync(esptool, ct);
            if (!detected)
                throw new TimeoutException("Bootloader not detected in time.");

            _log("Detected device. Flashing...");
            _setStatus("Flashing...", false, false);
            _setProgress(35);

            Exception? lastErr = null;
            for (int attempt = 0; attempt <= _cfg.RetryFlashCount; attempt++)
            {
                try
                {
                    var output = await esptool.WriteCloneAsync(ct);
                    ok++;
                    _setOk(ok);

                    _setProgress(100);
                    _setStatus("Done", false, true);

                    _log($"DONE #{ok}");
                    _log(Trim(output, 500));
                    _log("Disconnect/power-cycle RX and connect next.");
                    _log("--------------------------------------------------");

                    await Task.Delay(_cfg.BetweenDevicesDelayMs, ct);
                    lastErr = null;
                    break;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                    _setStatus("Flash error", true, false);
                    _setProgress(0);
                    _log($"FLASH ERROR (attempt {attempt + 1}/{_cfg.RetryFlashCount + 1}):");
                    _log(Trim(ex.Message, 600));
                    if (attempt < _cfg.RetryFlashCount)
                    {
                        _log("Retrying... keep RX in BOOT.");
                        await Task.Delay(800, ct);
                    }
                }
            }

            if (lastErr != null)
            {
                _log("Waiting for device again...");
                await Task.Delay(800, ct);
            }
        }
    }

    private async Task<bool> WaitForBootAsync(EsptoolService esptool, CancellationToken ct)
    {
        var start = Environment.TickCount64;
        string? lastMsg = null;

        while (!ct.IsCancellationRequested && (Environment.TickCount64 - start) < _cfg.BootWaitTimeoutMs)
        {
            try
            {
                var (ok, msg) = await esptool.TryChipIdAsync(ct);
                if (ok) return true;

                var shortMsg = ExtractShortReason(msg);
                if (!string.IsNullOrWhiteSpace(shortMsg) && shortMsg != lastMsg)
                {
                    _log("chip_id: " + shortMsg);
                    lastMsg = shortMsg;
                }
            }
            catch (Exception ex)
            {
                var m = Trim(ex.Message, 140);
                if (m != lastMsg)
                {
                    _log("chip_id error: " + m);
                    lastMsg = m;
                }
            }

            await Task.Delay(600, ct);
        }

        return false;
    }

    private void ValidateFiles()
    {
        foreach (var seg in _cfg.Profile.Segments.Where(s => s.Required))
        {
            if (!_cfg.BinPathsByLabel.TryGetValue(seg.Label, out var path) || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException($"Required file for segment '{seg.Label}' not found.", path);
        }
    }

    private static string ExtractShortReason(string output)
    {
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(l => l.Trim())
                          .ToList();

        var key = lines.FirstOrDefault(l =>
            l.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("No serial data", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Could not open port", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Invalid head of packet", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Timed out", StringComparison.OrdinalIgnoreCase));

        return key ?? (lines.LastOrDefault() ?? "");
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "...";
}
