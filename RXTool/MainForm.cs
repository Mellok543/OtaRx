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
        private readonly ComboBox _bind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
        private readonly ComboBox _receiver = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };

        private readonly TextBox _fw = new() { Width = 360, ReadOnly = true };
        private readonly Button _pickFw = new() { Text = "Выбрать firmware.bin", Width = 360, Height = 30 };

        private readonly Button _btnFlash = new() { Text = "Залить прошивку", Width = 360, Height = 38 };
        private readonly Button _btnSet = new() { Text = "Установить (Bind Phrase + Приемник)", Width = 360, Height = 38 };
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

            left.Controls.Add(new Label { Text = "Bind Phrase:", AutoSize = true, Padding = new Padding(0, 10, 0, 0) });
            left.Controls.Add(_bind);

            left.Controls.Add(new Label { Text = "Приемник:", AutoSize = true, Padding = new Padding(0, 10, 0, 0) });
            left.Controls.Add(_receiver);

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
            _btnFlash.Click += async (_, __) => await DoFlashOnly();
            _btnSet.Click += async (_, __) => await DoSetBindAndReceiverRequest();
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

                Log($"OK: Прошивок загружено: {_cfg.Firmwares.Count}");
                Log("Порядок: Прошивка -> Bind Phrase -> Приемник (его body request).");
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

            _bind.Items.Clear();
            foreach (var b in fw.BindPhrases)
                _bind.Items.Add(b.Name);
            if (_bind.Items.Count > 0) _bind.SelectedIndex = 0;

            _receiver.Items.Clear();
            foreach (var rx in fw.Receivers)
                _receiver.Items.Add(rx.Name);
            if (_receiver.Items.Count > 0) _receiver.SelectedIndex = 0;

            Log($"Прошивка выбрана: {fw.Name} | WiFi: {fw.Wifi.Match.Mode}={fw.Wifi.Match.Value}");
            Log($"Bind Phrase: {fw.BindPhrases.Count} | Приемники: {fw.Receivers.Count}");
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

        private BindPhrase? GetBindPhrase(FirmwareConfig fw)
        {
            var idx = _bind.SelectedIndex;
            if (idx < 0 || idx >= fw.BindPhrases.Count) return null;
            return fw.BindPhrases[idx];
        }

        private ReceiverConfig? GetReceiver(FirmwareConfig fw)
        {
            var idx = _receiver.SelectedIndex;
            if (idx < 0 || idx >= fw.Receivers.Count) return null;
            return fw.Receivers[idx];
        }

        private async Task<string> EnsureWifiAndPing(FirmwareConfig fw, CancellationToken ct)
        {
            Log("Жду сеть Wi-Fi по прошивке и подключаюсь...");

            var ssid = await WifiHelper.WaitAndConnectAsync(fw.Wifi.Match, fw.Wifi.Password, TimeSpan.FromMinutes(3), Log);
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
                await EnsureWifiAndPing(fw, _cts.Token);

                using var api = new RxApi();
                var prog = new Progress<int>(v => _progress.Value = Math.Clamp(v, 0, 100));

                Log("Заливаю прошивку (только firmware)...");
                await api.UploadAsync(fw.Upload, _fw.Text, Log, prog, _cts.Token);

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

        private async Task DoSetBindAndReceiverRequest()
        {
            var fw = GetFirmware();
            if (fw == null) { Log("ERROR: прошивка не выбрана"); return; }

            var b = GetBindPhrase(fw);
            if (b == null) { Log("ERROR: Bind Phrase не выбран"); return; }

            var r = GetReceiver(fw);
            if (r == null) { Log("ERROR: приемник не выбран"); return; }

            if (b.Uid == null || b.Uid.Length != 6 || b.Uid.Any(x => x < 0 || x > 255))
            {
                Log("ERROR: UID в конфиге должен быть массивом 6 чисел 0..255");
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true);

            try
            {
                await EnsureWifiAndPing(fw, _cts.Token);

                using var api = new RxApi();

                var bindJson = JsonTemplate.BuildBindJson(fw.BindRequest.Template, b.Uid);
                Log($"Устанавливаю Bind Phrase: {b.Name} ...");
                await api.PostJsonAsync(fw.BindRequest.Url, bindJson, "POST bind", Log, _cts.Token);

                var receiverJson = JsonSerializer.Serialize(r.Request.Body);
                Log($"Применяю запрос приемника: {r.Name} ...");
                await api.PostJsonAsync(r.Request.Url, receiverJson, "POST receiver", Log, _cts.Token);

                if (r.Request.NeedReboot && !string.IsNullOrWhiteSpace(r.Request.RebootUrl))
                {
                    Log("Reboot...");
                    await api.PostJsonAsync(r.Request.RebootUrl, "", "POST reboot", Log, _cts.Token);
                    Log("Жду перезагрузку 15 сек...");
                    await Task.Delay(15000, _cts.Token);
                }

                Log("DONE: Bind Phrase + настройки приемника применены.");
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
