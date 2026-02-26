namespace ElrsWifiBatchFlasher.Services;

public sealed class BatchConfig
{
    public string FirmwarePath { get; set; } = "";
    public string RxSsid { get; set; } = "ExpressLRS RX";      // можно как точное имя
    public string? RxPassword { get; set; } = "expresslrs";    // может быть null/"" если открытая
    public bool SsidIsPrefix { get; set; } = true;            // искать сеть по префиксу
    public int ConnectTimeoutMs { get; set; } = 20_000;
    public int HttpTimeoutMs { get; set; } = 20_000;
    public int AfterUploadWaitMs { get; set; } = 8_000;        // время на перезагрузку RX
}