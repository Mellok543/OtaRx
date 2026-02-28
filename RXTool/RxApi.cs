using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RxTool
{
    public sealed class RxApi : IDisposable
    {
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        public void Dispose() => _http.Dispose();

        public async Task<bool> PingAsync(string url)
        {
            try
            {
                using var r = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                return r.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task UploadAsync(UploadConfig up, string firmwarePath, Action<string> log, IProgress<int>? progress, CancellationToken ct)
        {
            var fi = new FileInfo(firmwarePath);
            if (!fi.Exists) throw new FileNotFoundException("Firmware not found", firmwarePath);

            log($"Upload -> {up.Url} | file={fi.Name} ({fi.Length} bytes)");

            using var fs = File.OpenRead(firmwarePath);

            // streaming + progress
            var streamContent = new ProgressStreamContent(fs, 64 * 1024, progress, fi.Length, ct);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var form = new MultipartFormDataContent();
            form.Add(streamContent, up.FileField, fi.Name);

            using var req = new HttpRequestMessage(HttpMethod.Post, up.Url) { Content = form };
            if (!string.IsNullOrWhiteSpace(up.FileSizeHeader))
                req.Headers.TryAddWithoutValidation(up.FileSizeHeader, fi.Length.ToString());

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            log($"/update => {(int)resp.StatusCode} {resp.ReasonPhrase} | {Trim(text)}");
            resp.EnsureSuccessStatusCode();
        }

        public async Task PostJsonAsync(string url, string json, string label, Action<string> log, CancellationToken ct)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(url, content, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            log($"{label} => {(int)resp.StatusCode} {resp.ReasonPhrase} | {Trim(text)}");
            resp.EnsureSuccessStatusCode();
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Replace("\r", "").Replace("\n", " ");
            return s.Length > 180 ? s.Substring(0, 180) + "..." : s;
        }
    }

    internal sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _stream;
        private readonly int _bufferSize;
        private readonly IProgress<int>? _progress;
        private readonly long _totalBytes;
        private readonly CancellationToken _ct;

        public ProgressStreamContent(Stream stream, int bufferSize, IProgress<int>? progress, long totalBytes, CancellationToken ct)
        {
            _stream = stream;
            _bufferSize = bufferSize;
            _progress = progress;
            _totalBytes = totalBytes;
            _ct = ct;
        }

        protected override async Task SerializeToStreamAsync(Stream target, System.Net.TransportContext? context)
        {
            var buffer = new byte[_bufferSize];
            long sent = 0;

            int read;
            while ((read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), _ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), _ct);
                sent += read;
                if (_progress != null && _totalBytes > 0)
                    _progress.Report((int)(sent * 100L / _totalBytes));
            }
            _progress?.Report(100);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _totalBytes;
            return true;
        }
    }
}