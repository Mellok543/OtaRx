using ElrsTtlBatchFlasher.Models;

namespace ElrsTtlBatchFlasher.Services;

public sealed class EsptoolService
{
    private readonly FlashConfig _cfg;

    public EsptoolService(FlashConfig cfg) => _cfg = cfg;

    private string BaseArgs(int baud) =>
        $"--chip {_cfg.Profile.Chip} --port {_cfg.Port} --baud {baud}";

    public async Task<(bool ok, string output)> TryChipIdAsync(CancellationToken ct)
    {
        var args = $"{BaseArgs(_cfg.DetectBaud)} --before no_reset --after no_reset chip_id";
        var (code, outp, err) = await ProcessRunner.RunAsync(_cfg.EsptoolPath, args, ct);
        var all = (outp + "\n" + err).Trim();

        var ok = code == 0 && all.Contains("Chip is", StringComparison.OrdinalIgnoreCase);
        return (ok, all);
    }

    public async Task<string> WriteCloneAsync(CancellationToken ct)
    {
        // Build write_flash args from profile segments
        var args = $"{BaseArgs(_cfg.Baud)} --before no_reset --after no_reset write_flash ";

        foreach (var seg in _cfg.Profile.Segments)
        {
            if (!_cfg.BinPathsByLabel.TryGetValue(seg.Label, out var binPath) || string.IsNullOrWhiteSpace(binPath))
            {
                if (seg.Required)
                    throw new InvalidOperationException($"Missing file for required segment: {seg.Label}");
                continue;
            }

            args += $"{seg.Offset} \"{binPath}\" ";
        }

        var (code, outp, err) = await ProcessRunner.RunAsync(_cfg.EsptoolPath, args, ct);
        var all = (outp + "\n" + err).Trim();

        if (code != 0)
            throw new InvalidOperationException(all);

        return all;
    }

    public async Task<string> ReadCloneAsync(string outputDir, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDir);

        // Read each segment into outputDir with label.bin
        foreach (var seg in _cfg.Profile.Segments)
        {
            var outFile = Path.Combine(outputDir, $"{seg.Label}.bin");
            var args = $"{BaseArgs(_cfg.Baud)} --before no_reset --after no_reset read_flash {seg.Offset} {seg.Size} \"{outFile}\"";

            var (code, outp, err) = await ProcessRunner.RunAsync(_cfg.EsptoolPath, args, ct);
            var all = (outp + "\n" + err).Trim();
            if (code != 0)
                throw new InvalidOperationException($"Read {seg.Label} failed:\n{all}");
        }

        return $"Clone saved to: {outputDir}";
    }

    public async Task<string> ReadMacAsync(CancellationToken ct)
    {
        var args = $"{BaseArgs(_cfg.DetectBaud)} --before no_reset --after no_reset read_mac";
        var (code, outp, err) = await ProcessRunner.RunAsync(_cfg.EsptoolPath, args, ct);
        var all = (outp + "\n" + err).Trim();
        if (code != 0) throw new InvalidOperationException(all);
        return all;
    }
}
