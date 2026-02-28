using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RxTool
{
    public sealed class MainForm : Form
    {
        private readonly ComboBox _firmware = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
        private readonly ComboBox _receiver = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
        private readonly ComboBox _bind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
        private readonly ComboBox _domain = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };

        private readonly TextBox _fw = new() { Width = 360, ReadOnly = true };
        private readonly Button _pickFw = new() { Text = "Выбрать firmware.bin", Width = 360, Height = 30 };

        private readonly Button _btnFlash = new() { Text = "Залить прошивку", Width = 360, Height = 38 };
        private readonly Button _btnSet = new() { Text = "Установить (Bind Phrase + Частота)", Width = 360, Height = 38 };
        private readonly Button _btnStop = new() { Text = "STOP", Width = 360, Height = 30, Enabled = false };

        private readonly ProgressBar _progress = new() { Width = 360, Height = 18 };
        private readonly TextBox _log = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Dock = DockStyle.Fill };

        private AppConfig _cfg = new();
        private CancellationTokenSource? _cts;

        public MainForm()
        {
            Text = "Mell Tool";
            Width = 980;
            Height = 620;
            MinimumSize = new System.Drawing.Size(980, 620);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;

            var left = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                Width = 420,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(12)
            };

            left.Controls.Add(new Label { Text = "Прошивка:", AutoSize = true });
            left.Controls.Add(_firmware);

            left.Controls.Add(new Label { Text = "Тип приемника:", AutoSize = true, Padding = new Padding(0, 10, 0, 0) });
            left.Controls.Add(_receiver);

            left.Controls.Add(new Label { Text = "Bind Phrase:", AutoSize = true, Padding = new Padding(0, 10, 0, 0) });
            left.Controls.Add(_bind);

            left.Controls.Add(new Label { Text = "Частота / Regulatory Domain:", AutoSize = true, Padding = new Padding(0, 10, 0, 0) });
            left.Controls.Add(_domain);

            left.Controls.Add(new Label { Text = "Firmware (.bin):", AutoSize = true, Padding = new Padding(0, 10, 0, 0) });
            left.Controls.Add(_fw);
            left.Controls.Add(_pickFw);

            left.Controls.Add(new Label { Text = "Прогресс:", AutoSize = true, Padding = new Padding(0, 10, 0, 0) });
            left.Controls.Add(_progress);

            left.Controls.Add(_btnFlash);
            left.Controls.Add(_btnSet);
            left.Controls.Add(_btnStop);

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            right.Controls.Add(_log);

            Controls.Add(right);
            Controls.Add(left);

            _pickFw.Click += (_, __) => PickFirmware();
            _firmware.SelectedIndexChanged += (_, __) => RefreshFirmwareDependentLists();
            _receiver.SelectedIndexChanged += (_, __) => RefreshReceiverDependentLists();
            _btnFlash.Click += async (_, __) => await DoFlashOnly();
            _btnSet.Click += async (_, __) => await DoSetBindAndDomain();
            _btnStop.Click += (_, __) => _cts?.Cancel();

            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "config.json");
                if (!File.Exists(path))
                {
                    Log("ERROR: config.json не найден рядом с exe.");
                    return;
                }

                var json = File.ReadAllText(path, Encoding.UTF8);
                _cfg = AppConfig.FromJson(json);

                _firmware.Items.Clear();
                foreach (var fw in _cfg.Firmwares)
                    _firmware.Items.Add(fw.Name);

                if (_firmware.Items.Count > 0) _firmware.SelectedIndex = 0;

                // Если конфиг в простом формате — прошивка одна общая,
                // оставляем выбор только приемника.
                var singleFirmware = _cfg.Firmwares.Count == 1;
                _firmware.Enabled = !singleFirmware;

                Log($"OK: Прошивок загружено: {_cfg.Firmwares.Count}");
                Log($"");
                Log($"");
                Log($"");
                Log($"");
                Log($"Mell старается для вас)) Успешного пользования!!!!!!!");
            }
            catch (Exception ex)
            {
                Log("ERROR config.json: " + ex.Message);
            }
        }

        private void RefreshFirmwareDependentLists()
        {
            var fw = GetFirmware();
            if (fw == null) return;

            _receiver.Items.Clear();
            foreach (var rx in fw.Receivers)
                _receiver.Items.Add(rx.Name);

            if (_receiver.Items.Count > 0) _receiver.SelectedIndex = 0;

            Log($"Прошивка выбрана: {fw.Name} | Приемников: {fw.Receivers.Count}");
        }

        private void RefreshReceiverDependentLists()
        {
            var rx = GetReceiver();
            if (rx == null) return;

            _bind.Items.Clear();
            foreach (var b in rx.BindPhrases) _bind.Items.Add(b.Name);
            if (_bind.Items.Count > 0) _bind.SelectedIndex = 0;

            _domain.Items.Clear();
            foreach (var d in rx.RegulatoryDomains) _domain.Items.Add(d.Name);
            if (_domain.Items.Count > 0) _domain.SelectedIndex = 0;

            Log($"Приемник выбран: {rx.Name} | WiFi match: {rx.Wifi.Match.Mode}={rx.Wifi.Match.Value}");
        }

        private void PickFirmware()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "BIN files (*.bin)|*.bin|All files (*.*)|*.*",
                Title = "Выбери прошивку (firmware.bin)"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                _fw.Text = dlg.FileName;
        }

        private FirmwareConfig? GetFirmware()
        {
            var idx = _firmware.SelectedIndex;
            if (idx < 0 || idx >= _cfg.Firmwares.Count) return null;
            return _cfg.Firmwares[idx];
        }

        private ReceiverConfig? GetReceiver()
        {
            var fw = GetFirmware();
            if (fw == null) return null;

            var idx = _receiver.SelectedIndex;
            if (idx < 0 || idx >= fw.Receivers.Count) return null;
            return fw.Receivers[idx];
        }

        private BindPhrase? GetBindPhrase(ReceiverConfig r)
        {
            var idx = _bind.SelectedIndex;
            if (idx < 0 || idx >= r.BindPhrases.Count) return null;
            return r.BindPhrases[idx];
        }

        private RegDomain? GetDomain(ReceiverConfig r)
        {
            var idx = _domain.SelectedIndex;
            if (idx < 0 || idx >= r.RegulatoryDomains.Count) return null;
            return r.RegulatoryDomains[idx];
        }

        private static UploadConfig ResolveUpload(FirmwareConfig fw, ReceiverConfig r)
            => r.Upload ?? fw.Upload;

        private async Task<string> EnsureWifiAndPing(ReceiverConfig r, CancellationToken ct)
        {
            Log("Жду сеть Wi-Fi по приемнику и подключаюсь...");

            var ssid = await WifiHelper.WaitAndConnectAsync(r.Wifi.Match, r.Wifi.Password, TimeSpan.FromMinutes(3), Log);
            if (ssid == null) throw new Exception("Не дождался Wi-Fi сети / не смог подключиться");

            using var api = new RxApi();
            Log("Проверяю доступность http://10.0.0.1 ...");
            var ok = await api.PingAsync("http://10.0.0.1/");
            if (!ok) throw new Exception("RX не отвечает на http://10.0.0.1 (проверь, что подключение реально к RX)");

            return ssid;
        }

        private void SetBusy(bool busy)
        {
            _btnFlash.Enabled = !busy;
            _btnSet.Enabled = !busy;
            _btnStop.Enabled = busy;
        }

        private async Task DoFlashOnly()
        {
            var fw = GetFirmware();
            if (fw == null) { Log("ERROR: прошивка не выбрана"); return; }

            var r = GetReceiver();
            if (r == null) { Log("ERROR: приемник не выбран"); return; }

            if (string.IsNullOrWhiteSpace(_fw.Text) || !File.Exists(_fw.Text))
            {
                Log("ERROR: выбери firmware.bin");
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true);
            _progress.Value = 0;

            try
            {
                await EnsureWifiAndPing(r, _cts.Token);

                using var api = new RxApi();
                var prog = new Progress<int>(v => _progress.Value = Math.Clamp(v, 0, 100));
                var upload = ResolveUpload(fw, r);

                Log("Заливаю прошивку (только firmware)...");
                await api.UploadAsync(upload, _fw.Text, Log, prog, _cts.Token);

                Log("DONE: Прошивка залита.");
            }
            catch (OperationCanceledException)
            {
                Log("STOP: отменено.");
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _progress.Value = 0;
                SetBusy(false);
            }
        }

        private async Task DoSetBindAndDomain()
        {
            var r = GetReceiver();
            if (r == null) { Log("ERROR: приемник не выбран"); return; }

            var b = GetBindPhrase(r);
            if (b == null) { Log("ERROR: Bind Phrase не выбран"); return; }

            var d = GetDomain(r);
            if (d == null) { Log("ERROR: Regulatory Domain не выбран"); return; }

            if (b.Uid == null || b.Uid.Length != 6 || b.Uid.Any(x => x < 0 || x > 255))
            {
                Log("ERROR: UID в конфиге должен быть массивом 6 чисел 0..255");
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true);

            try
            {
                await EnsureWifiAndPing(r, _cts.Token);

                using var api = new RxApi();

                // bind json from template + UID
                var bindJson = JsonTemplate.BuildBindJson(r.BindRequest.Template, b.Uid);
                Log($"Устанавливаю Bind Phrase: {b.Name} ...");
                await api.PostJsonAsync(r.BindRequest.Url, bindJson, "POST bind", Log, _cts.Token);

                // domain
                var domainJson = JsonSerializer.Serialize(d.Request.Body);
                Log($"Устанавливаю частоту/домен: {d.Name} ...");
                await api.PostJsonAsync(d.Request.Url, domainJson, "POST domain", Log, _cts.Token);

                if (d.After.NeedReboot && !string.IsNullOrWhiteSpace(d.After.RebootUrl))
                {
                    Log("Reboot...");
                    await api.PostJsonAsync(d.After.RebootUrl, "", "POST reboot", Log, _cts.Token);
                    Log("Жду перезагрузку 15 сек...");
                    await Task.Delay(15000, _cts.Token);
                }

                Log("DONE: Bind Phrase + Domain применены.");
            }
            catch (OperationCanceledException)
            {
                Log("STOP: отменено.");
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false);
            }
        }

        private void Log(string msg)
        {
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        }
    }
}
