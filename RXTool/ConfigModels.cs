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

        // Bind запрос для всех Bind Phrase внутри одной прошивки всегда одинаковый.
        [JsonPropertyName("bindRequest")]
        public BindRequest BindRequest { get; set; } = new();

        [JsonPropertyName("bindPhrases")]
        public List<BindPhrase> BindPhrases { get; set; } = new();

        // У каждого приемника свой body request.
        [JsonPropertyName("receivers")]
        public List<ReceiverConfig> Receivers { get; set; } = new();
    }

    public sealed class ReceiverConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("request")]
        public ReceiverRequest Request { get; set; } = new();
    }

    public sealed class ReceiverRequest
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "http://10.0.0.1/options.json";

        [JsonPropertyName("body")]
        public Dictionary<string, JsonElement> Body { get; set; } = new();

        [JsonPropertyName("needReboot")]
        public bool NeedReboot { get; set; } = true;

        [JsonPropertyName("rebootUrl")]
        public string RebootUrl { get; set; } = "http://10.0.0.1/reboot";
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
        public string Mode { get; set; } = "exact"; // exact|startsWith|regex

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
