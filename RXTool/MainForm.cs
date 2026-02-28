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
        private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
        private readonly ComboBox _bind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
        private readonly ComboBox _domain = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };

        private readonly TextBox _fw = new() { Width = 360, ReadOnly = true };
        private readonly Button _pickFw = new() { Text = "Выбрать firmware.bin", Width = 360, Height = 30 };

        private readonly Button _btnFlash = new() { Text = "Залить прошивку", Width = 360, Height = 38 };
        private readonly Button _btnSet = new() { Text = "Установить (Bind Phrase + Частота)", Width = 360, Height = 38 };
        private readonly Button _btnStop = new() { Text = "STOP", Width = 360, Height = 30, Enabled = false };

        private readonly ProgressBar _progress = new() { Width = 360, Height = 18 };
        private readonly TextBox _log = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Width = 560, Height = 360 };

        private AppConfig _cfg = new();
        private CancellationTokenSource? _cts;

        public MainForm()
        {
            Text = "Mell Tool";
            Width = 980;
            Height = 520;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            var left = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                Width = 420,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(12)
            };

            left.Controls.Add(new Label { Text = "Прошивка/Профиль:", AutoSize = true });
            left.Controls.Add(_profile);

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
            _profile.SelectedIndexChanged += (_, __) => RefreshProfileDependentLists();
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
                _cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();

                _profile.Items.Clear();
                foreach (var p in _cfg.Profiles)
                    _profile.Items.Add(p.Name);

                if (_profile.Items.Count > 0) _profile.SelectedIndex = 0;

                Log($"OK: Профилей загружено: {_cfg.Profiles.Count}");
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

        private void RefreshProfileDependentLists()
        {
            var p = GetProfile();
            if (p == null) return;

            _bind.Items.Clear();
            foreach (var b in p.BindPhrases) _bind.Items.Add(b.Name);
            if (_bind.Items.Count > 0) _bind.SelectedIndex = 0;

            _domain.Items.Clear();
            foreach (var d in p.RegulatoryDomains) _domain.Items.Add(d.Name);
            if (_domain.Items.Count > 0) _domain.SelectedIndex = 0;

            Log($"Профиль выбран: {p.Name} | WiFi match: {p.Wifi.Match.Mode}={p.Wifi.Match.Value}");
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

        private FwProfile? GetProfile()
        {
            var idx = _profile.SelectedIndex;
            if (idx < 0 || idx >= _cfg.Profiles.Count) return null;
            return _cfg.Profiles[idx];
        }

        private BindPhrase? GetBindPhrase(FwProfile p)
        {
            var idx = _bind.SelectedIndex;
            if (idx < 0 || idx >= p.BindPhrases.Count) return null;
            return p.BindPhrases[idx];
        }

        private RegDomain? GetDomain(FwProfile p)
        {
            var idx = _domain.SelectedIndex;
            if (idx < 0 || idx >= p.RegulatoryDomains.Count) return null;
            return p.RegulatoryDomains[idx];
        }

        private async Task<string> EnsureWifiAndPing(FwProfile p, CancellationToken ct)
        {
            Log("Жду сеть Wi-Fi по профилю и подключаюсь...");

            var ssid = await WifiHelper.WaitAndConnectAsync(p.Wifi.Match, p.Wifi.Password, TimeSpan.FromMinutes(3), Log);
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
            var p = GetProfile();
            if (p == null) { Log("ERROR: профиль не выбран"); return; }

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
                await EnsureWifiAndPing(p, _cts.Token);

                using var api = new RxApi();
                var prog = new Progress<int>(v => _progress.Value = Math.Clamp(v, 0, 100));

                Log("Заливаю прошивку (только firmware)...");
                await api.UploadAsync(p.Upload, _fw.Text, Log, prog, _cts.Token);

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
            var p = GetProfile();
            if (p == null) { Log("ERROR: профиль не выбран"); return; }

            var b = GetBindPhrase(p);
            if (b == null) { Log("ERROR: Bind Phrase не выбран"); return; }

            var d = GetDomain(p);
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
                await EnsureWifiAndPing(p, _cts.Token);

                using var api = new RxApi();

                // bind json from template + UID
                var bindJson = JsonTemplate.BuildBindJson(p.BindRequest.Template, b.Uid);
                Log($"Устанавливаю Bind Phrase: {b.Name} ...");
                await api.PostJsonAsync(p.BindRequest.Url, bindJson, "POST bind", Log, _cts.Token);

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