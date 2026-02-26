using System.Windows;
using Microsoft.Win32;
using ElrsWifiBatchFlasher.Services;

namespace ElrsWifiBatchFlasher;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        OkCountText.Text = "0";
    }

    private void PickFirmware_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "BIN files (*.bin)|*.bin|All files (*.*)|*.*",
            Title = "Select firmware.bin"
        };
        if (dlg.ShowDialog() == true)
            FirmwarePathTextBox.Text = dlg.FileName;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) return;

        var cfg = new BatchConfig
        {
            FirmwarePath = FirmwarePathTextBox.Text.Trim(),
            RxSsid = SsidTextBox.Text.Trim(),
            RxPassword = PasswordTextBox.Text, // can be empty
            SsidIsPrefix = PrefixCheckBox.IsChecked == true,
        };

        if (int.TryParse(AfterWaitTextBox.Text.Trim(), out var ms) && ms > 0)
            cfg.AfterUploadWaitMs = ms;

        _cts = new CancellationTokenSource();

        SetBusy(true);
        Log("=== START ===");

        try
        {
            var wifi = new NetshWifiService();
            var runner = new BatchRunner(wifi);

            await runner.RunAsync(
                cfg,
                log: Log,
                setOkCount: n => Dispatcher.Invoke(() => OkCountText.Text = n.ToString()),
                ct: _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Canceled.");
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
            Log("=== STOP ===");
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void SetBusy(bool busy)
    {
        StartButton.IsEnabled = !busy;
        StopButton.IsEnabled = busy;
    }

    private void Log(string s)
    {
        Dispatcher.Invoke(() =>
        {
            LogTextBox.AppendText(s + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
    }
}