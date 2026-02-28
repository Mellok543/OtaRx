using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RxTool
{
    public sealed class AppConfig
    {
        [JsonPropertyName("firmwares")]
        public List<FirmwareConfig> Firmwares { get; set; } = new();

        public static AppConfig FromJson(string json)
            => JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }

    public sealed class FirmwareConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("wifi")]
        public WifiConfig Wifi { get; set; } = new();

        [JsonPropertyName("upload")]
        public UploadConfig Upload { get; set; } = new();

        // Bind запрос одинаковый для всех bind phrase в рамках прошивки.
        [JsonPropertyName("bindRequest")]
        public BindRequest BindRequest { get; set; } = new();

        [JsonPropertyName("bindPhrases")]
        public List<BindPhrase> BindPhrases { get; set; } = new();

        [JsonPropertyName("receivers")]
        public List<ReceiverConfig> Receivers { get; set; } = new();
    }

    public sealed class ReceiverConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        // Базовый запрос приемника (без частоты).
        [JsonPropertyName("request")]
        public ReceiverRequest Request { get; set; } = new();

        // Частоты выбираются отдельно после выбора приемника.
        [JsonPropertyName("frequencies")]
        public List<FrequencyPreset> Frequencies { get; set; } = new();
    }

    public sealed class ReceiverRequest
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "http://10.0.0.1/options.json";

        [JsonPropertyName("baseBody")]
        public Dictionary<string, JsonElement> BaseBody { get; set; } = new();

        [JsonPropertyName("needReboot")]
        public bool NeedReboot { get; set; } = true;

        [JsonPropertyName("rebootUrl")]
        public string RebootUrl { get; set; } = "http://10.0.0.1/reboot";
    }

    public sealed class FrequencyPreset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("freq1")]
        public int? Freq1 { get; set; }

        [JsonPropertyName("freq2")]
        public int? Freq2 { get; set; }
    }

    public sealed class WifiConfig
    {
        [JsonPropertyName("match")]
        public WifiMatch Match { get; set; } = new();

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";
    }

    public sealed class WifiMatch
    {
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "exact";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "";
    }

    public sealed class UploadConfig
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "http://10.0.0.1/update";

        [JsonPropertyName("fileField")]
        public string FileField { get; set; } = "upload";

        [JsonPropertyName("fileSizeHeader")]
        public string FileSizeHeader { get; set; } = "X-FileSize";
    }

    public sealed class BindPhrase
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("uid")]
        public int[] Uid { get; set; } = new int[6];
    }

    public sealed class BindRequest
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "http://10.0.0.1/config";

        [JsonPropertyName("template")]
        public JsonElement Template { get; set; }
    }
}
