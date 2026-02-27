using System.IO.Ports;
using System.Text.Json;
using Microsoft.Win32;
using ElrsTtlBatchFlasher.Models;
using ElrsTtlBatchFlasher.Services;

namespace ElrsTtlBatchFlasher;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;
    private List<ReceiverProfile> _profiles = new();

    public MainWindow()
    {
        InitializeComponent();
        RefreshPorts();
        LoadProfiles();
        SetStatus("Idle");
    }

    // ===============================
    // UI Helpers
    // ===============================

    private void Log(string text)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(text + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private void SetStatus(string text, bool isError = false, bool isSuccess = false)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = text;

            if (isError)
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(239, 68, 68));
            else if (isSuccess)
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(34, 197, 94));
            else
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(100, 116, 139));
        });
    }

    private void SetProgress(double value)
    {
        Dispatcher.Invoke(() => Progress.Value = Math.Max(0, Math.Min(100, value)));
    }

    private void SetBusy(bool busy)
    {
        Dispatcher.Invoke(() =>
        {
            StartBtn.IsEnabled = !busy;
            ReadCloneBtn.IsEnabled = !busy;
            StopBtn.IsEnabled = busy;
            ReceiverCombo.IsEnabled = !busy;
            PortCombo.IsEnabled = !busy;
        });
    }

    // ===============================
    // COM Ports
    // ===============================

    private void RefreshPorts()
    {
        PortCombo.ItemsSource = SerialPort.GetPortNames().OrderBy(x => x).ToList();
        if (PortCombo.Items.Count > 0 && PortCombo.SelectedIndex < 0)
            PortCombo.SelectedIndex = 0;
    }

    private void RefreshCom_Click(object sender, RoutedEventArgs e)
    {
        RefreshPorts();
        Log("COM list refreshed.");
    }

    // ===============================
    // Profiles
    // ===============================

    private void LoadProfiles()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _profiles = ProfilesService.LoadProfiles(baseDir);

            ReceiverCombo.ItemsSource = _profiles;
            if (_profiles.Count > 0)
                ReceiverCombo.SelectedIndex = 0;

            Log($"Loaded profiles: {_profiles.Count}");
        }
        catch (Exception ex)
        {
            Log("ERROR loading receivers.json: " + ex.Message);
        }
    }

    // ===============================
    // File Pickers
    // ===============================

    private void PickInto(System.Windows.Controls.TextBox box)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "BIN files (*.bin)|*.bin|All files (*.*)|*.*",
            Title = "Select .bin file"
        };
        if (dlg.ShowDialog() == true)
            box.Text = dlg.FileName;
    }

    private void PickApp_Click(object sender, RoutedEventArgs e) => PickInto(AppBox);
    private void PickNvs_Click(object sender, RoutedEventArgs e) => PickInto(NvsBox);
    private void PickOta_Click(object sender, RoutedEventArgs e) => PickInto(OtaBox);
    private void PickSpiffs_Click(object sender, RoutedEventArgs e) => PickInto(SpiffsBox);

    // ===============================
    // Build Config
    // ===============================

    private FlashConfig BuildConfig()
    {
        if (ReceiverCombo.SelectedItem is not ReceiverProfile profile)
            throw new InvalidOperationException("Select receiver profile.");

        if (PortCombo.SelectedItem is not string port)
            throw new InvalidOperationException("Select COM port.");

        if (!int.TryParse(BaudBox.Text.Trim(), out var baud) || baud <= 0)
            baud = 921600;

        var cfg = new FlashConfig
        {
            EsptoolPath = EsptoolPathBox.Text.Trim(),
            Port = port,
            Baud = baud,
            DetectBaud = 115200,
            Profile = profile,
        };

        // Map UI fields to segment labels (common labels)
        cfg.BinPathsByLabel["app0"] = AppBox.Text.Trim();
        cfg.BinPathsByLabel["app"] = AppBox.Text.Trim();
        cfg.BinPathsByLabel["nvs"] = NvsBox.Text.Trim();
        cfg.BinPathsByLabel["otadata"] = OtaBox.Text.Trim();
        cfg.BinPathsByLabel["spiffs"] = SpiffsBox.Text.Trim();

        return cfg;
    }

    // ===============================
    // Start / Stop
    // ===============================

    private async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) return;

        FlashConfig cfg;
        try
        {
            cfg = BuildConfig();
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        SetProgress(5);
        SetStatus("Starting...");
        Log("=== START ===");

        try
        {
            var runner = new BatchRunner(
                cfg,
                log: Log,
                setOk: n => Dispatcher.Invoke(() => OkCountText.Text = n.ToString()),
                setProgress: SetProgress,
                setStatus: SetStatus
            );

            await runner.RunAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Stopped");
            Log("Canceled.");
        }
        catch (Exception ex)
        {
            SetStatus("Error", isError: true);
            SetProgress(0);
            Log("ERROR: " + ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
            Log("=== STOP ===");
        }
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ===============================
    // Read Clone
    // ===============================

    private async void ReadCloneBtn_Click(object sender, RoutedEventArgs e)
    {
        FlashConfig cfg;
        try
        {
            cfg = BuildConfig();
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            return;
        }

        using var folderDialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to save clone (.bin files)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        SetBusy(true);
        SetStatus("Reading clone...");
        SetProgress(10);
        Log("=== READ CLONE ===");
        Log("Target folder: " + folderDialog.SelectedPath);

        try
        {
            var esptool = new EsptoolService(cfg);

            // Optional: read MAC to create subfolder
            string macText = "";
            try { macText = await esptool.ReadMacAsync(CancellationToken.None); } catch { /* ignore */ }

            var safeFolder = folderDialog.SelectedPath;
            if (!string.IsNullOrWhiteSpace(macText))
            {
                var mac = ExtractMac(macText);
                if (!string.IsNullOrWhiteSpace(mac))
                {
                    safeFolder = Path.Combine(folderDialog.SelectedPath, $"clone_{mac.Replace(':','-')}");
                    Directory.CreateDirectory(safeFolder);
                    Log("MAC: " + mac);
                }
            }

            var result = await esptool.ReadCloneAsync(safeFolder, CancellationToken.None);
            SetProgress(100);
            SetStatus("Clone saved", isSuccess: true);
            Log(result);
        }
        catch (Exception ex)
        {
            SetStatus("Read error", isError: true);
            SetProgress(0);
            Log("ERROR: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            Log("=== END READ CLONE ===");
        }
    }

    private static string ExtractMac(string text)
    {
        // Common esptool output: "MAC: xx:xx:xx:xx:xx:xx"
        var m = System.Text.RegularExpressions.Regex.Match(text, @"MAC:\s*([0-9a-fA-F:]{17})");
        return m.Success ? m.Groups[1].Value : "";
    }
}
