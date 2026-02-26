using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace ElrsWifiBatchFlasher.Services;

public sealed class ElrsOtaService
{
    private readonly HttpClient _http;

    public ElrsOtaService(int timeoutMs)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false,
            Proxy = null
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs)
        };
    }

    public async Task<bool> WaitWebAsync(CancellationToken ct)
    {
        var url = "http://10.0.0.1/";
        var start = Environment.TickCount64;
        while (!ct.IsCancellationRequested && Environment.TickCount64 - start < 20_000)
        {
            try
            {
                using var resp = await _http.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { /* retry */ }

            await Task.Delay(500, ct);
        }
        return false;
    }

    public async Task <(string uploadPath, string fieldName)> DetectUploadFormAsync(CancellationToken ct)
    {
        var html = await _http.GetStringAsync("http://10.0.0.1/", ct);

        // action="/update"
        var action = "/update";
        var mAction = Regex.Match(html, @"<form[^>]*action\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (mAction.Success)
        {
            action = mAction.Groups[1].Value.Trim();
            if (!action.StartsWith("/")) action = "/" + action;
        }

        // input type="file" name="upload"
        var field = "upload"; // дефолт ELRS
        var mField = Regex.Match(html, @"<input[^>]*type\s*=\s*[""']file[""'][^>]*name\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (mField.Success)
            field = mField.Groups[1].Value.Trim();

        return (action, field);
    }

    public async Task<string> UploadFirmwareAsync(string firmwarePath, CancellationToken ct)
    {
        var (uploadPath, fieldName) = await DetectUploadFormAsync(ct);
        var url = "http://10.0.0.1" + uploadPath;

        var fi = new FileInfo(firmwarePath);
        var fileSize = fi.Length;

        using var fs = File.OpenRead(firmwarePath);
        using var form = new MultipartFormDataContent();

        // некоторые версии ELRS любят видеть file_name
        form.Add(new StringContent(fi.Name), "file_name");

        var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        // ВАЖНО: имя поля берём из HTML (обычно "upload")
        form.Add(fileContent, fieldName, fi.Name);

        // ВАЖНО: ELRS WebUpdater часто использует X-FileSize
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("X-FileSize", fileSize.ToString());
        req.Content = form;

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {body}");

        return body;
    }
}