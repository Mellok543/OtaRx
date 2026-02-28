using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RxTool
{
    public sealed class AppConfig
    {
        [JsonPropertyName("firmwares")]
        public List<FirmwareConfig> Firmwares { get; set; } = new();

        // Новый простой формат:
        // {
        //   "upload": {...},
        //   "receivers": [...]
        // }
        [JsonPropertyName("upload")]
        public UploadConfig? Upload { get; set; }

        [JsonPropertyName("receivers")]
        public List<SimpleReceiverConfig> Receivers { get; set; } = new();

        public static AppConfig FromJson(string json)
        {
            var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();

            if (cfg.Firmwares.Count > 0)
                return cfg;

            if (cfg.Receivers.Count == 0)
                return cfg;

            var upload = cfg.Upload ?? new UploadConfig();
            var fw = new FirmwareConfig
            {
                Id = "default",
                Name = "Общая прошивка",
                Upload = upload,
                Receivers = cfg.Receivers.Select(MapSimpleReceiver).ToList()
            };

            cfg.Firmwares.Add(fw);
            return cfg;
        }

        private static ReceiverConfig MapSimpleReceiver(SimpleReceiverConfig s)
        {
            var matchMode = string.IsNullOrWhiteSpace(s.Wifi.Match) ? "exact" : s.Wifi.Match;
            var bindTemplate = s.Bind.BodyTemplate.ValueKind == JsonValueKind.Undefined
                ? JsonDocument.Parse("{\"uid\":\"$UID6\"}").RootElement
                : s.Bind.BodyTemplate;

            return new ReceiverConfig
            {
                Id = string.IsNullOrWhiteSpace(s.Id) ? s.Name : s.Id,
                Name = s.Name,
                Upload = s.Upload,
                Wifi = new WifiConfig
                {
                    Password = s.Wifi.Password,
                    Match = new WifiMatch
                    {
                        Mode = matchMode,
                        Value = s.Wifi.Ssid
                    }
                },
                BindPhrases = s.BindPhrases,
                BindRequest = new BindRequest
                {
                    Url = string.IsNullOrWhiteSpace(s.Bind.Url) ? "http://10.0.0.1/config" : s.Bind.Url,
                    Template = bindTemplate
                },
                RegulatoryDomains = s.Domains.Select(d => new RegDomain
                {
                    Name = d.Name,
                    Request = new RegRequest
                    {
                        Url = string.IsNullOrWhiteSpace(d.Url) ? "http://10.0.0.1/options.json" : d.Url,
                        Body = d.Body
                    },
                    After = new RegAfter
                    {
                        NeedReboot = d.NeedReboot,
                        RebootUrl = string.IsNullOrWhiteSpace(d.RebootUrl) ? "http://10.0.0.1/reboot" : d.RebootUrl
                    }
                }).ToList()
            };
        }
    }

    public sealed class FirmwareConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("upload")]
        public UploadConfig Upload { get; set; } = new();

        [JsonPropertyName("receivers")]
        public List<ReceiverConfig> Receivers { get; set; } = new();
    }

    public sealed class ReceiverConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("wifi")]
        public WifiConfig Wifi { get; set; } = new();

        [JsonPropertyName("upload")]
        public UploadConfig? Upload { get; set; }

        [JsonPropertyName("bindPhrases")]
        public List<BindPhrase> BindPhrases { get; set; } = new();

        [JsonPropertyName("bindRequest")]
        public BindRequest BindRequest { get; set; } = new();

        [JsonPropertyName("regulatoryDomains")]
        public List<RegDomain> RegulatoryDomains { get; set; } = new();
    }

    // Упрощенные модели для "пользовательского" конфига
    public sealed class SimpleReceiverConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("wifi")]
        public SimpleWifiConfig Wifi { get; set; } = new();

        [JsonPropertyName("upload")]
        public UploadConfig? Upload { get; set; }

        [JsonPropertyName("bindPhrases")]
        public List<BindPhrase> BindPhrases { get; set; } = new();

        [JsonPropertyName("bind")]
        public SimpleBindConfig Bind { get; set; } = new();

        [JsonPropertyName("domains")]
        public List<SimpleDomainConfig> Domains { get; set; } = new();
    }

    public sealed class SimpleWifiConfig
    {
        [JsonPropertyName("ssid")]
        public string Ssid { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("match")]
        public string Match { get; set; } = "exact";
    }

    public sealed class SimpleBindConfig
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "http://10.0.0.1/config";

        [JsonPropertyName("bodyTemplate")]
        public JsonElement BodyTemplate { get; set; }
    }

    public sealed class SimpleDomainConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

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

    public sealed class RegDomain
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("request")]
        public RegRequest Request { get; set; } = new();

        [JsonPropertyName("after")]
        public RegAfter After { get; set; } = new();
    }

    public sealed class RegRequest
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "http://10.0.0.1/options.json";

        [JsonPropertyName("body")]
        public Dictionary<string, JsonElement> Body { get; set; } = new();
    }

    public sealed class RegAfter
    {
        [JsonPropertyName("needReboot")]
        public bool NeedReboot { get; set; } = true;

        [JsonPropertyName("rebootUrl")]
        public string RebootUrl { get; set; } = "http://10.0.0.1/reboot";
    }
}
