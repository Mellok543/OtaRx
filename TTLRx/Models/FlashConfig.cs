namespace ElrsTtlBatchFlasher.Models;

public sealed class FlashConfig
{
    public string EsptoolPath { get; set; } = "esptool";
    public string Port { get; set; } = "";
    public int Baud { get; set; } = 921600;
    public int DetectBaud { get; set; } = 115200;

    public ReceiverProfile Profile { get; set; } = null!;

    // Paths to selected .bin files by label (app0, nvs, otadata, spiffs, etc)
    public Dictionary<string, string> BinPathsByLabel { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int BetweenDevicesDelayMs { get; set; } = 1500;
    public int BootWaitTimeoutMs { get; set; } = 30000;
    public int RetryFlashCount { get; set; } = 1; // additional retries after first failure
}
