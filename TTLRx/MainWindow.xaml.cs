using System.IO.Ports;
using Microsoft.Win32;
using ElrsTtlBatchFlasher.Models;
using ElrsTtlBatchFlasher.Services;
using System.Windows.Controls;

namespace ElrsTtlBatchFlasher;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, TextBox> _segmentPathBoxes = new(StringComparer.OrdinalIgnoreCase);

    private static readonly System.Windows.Media.SolidColorBrush ErrorBrush =
        new(System.Windows.Media.Color.FromRgb(239, 68, 68));

    private static readonly System.Windows.Media.SolidColorBrush SuccessBrush =
        new(System.Windows.Media.Color.FromRgb(34, 197, 94));

    private static readonly System.Windows.Media.SolidColorBrush MutedBrush =
        new(System.Windows.Media.Color.FromRgb(100, 116, 139));

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
            StatusText.Foreground = isError
                ? ErrorBrush
                : isSuccess
                    ? SuccessBrush
                    : MutedBrush;
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
        try
        {
            PortCombo.ItemsSource = SerialPort.GetPortNames().OrderBy(x => x).ToList();
            if (PortCombo.Items.Count > 0 && PortCombo.SelectedIndex < 0)
                PortCombo.SelectedIndex = 0;
        }
        catch (PlatformNotSupportedException)
        {
            PortCombo.ItemsSource = Array.Empty<string>();
            SetStatus("COM scan unavailable on this platform", isError: true);
            Log("WARNING: SerialPort.GetPortNames is not supported on this platform.");
        }
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
            var profiles = ProfilesService.LoadProfiles(baseDir);

            ReceiverCombo.ItemsSource = profiles;
            if (profiles.Count > 0)
                ReceiverCombo.SelectedIndex = 0;

            ReceiverCombo.SelectionChanged -= ReceiverCombo_SelectionChanged;
            ReceiverCombo.SelectionChanged += ReceiverCombo_SelectionChanged;
            RenderSegmentInputs();

            Log($"Loaded profiles: {profiles.Count}");
        }
        catch (Exception ex)
        {
            Log("ERROR loading receivers.json: " + ex.Message);
        }
    }

    // ===============================
    // File Pickers
    // ===============================

    private void PickInto(TextBox box)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "BIN files (*.bin)|*.bin|All files (*.*)|*.*",
            Title = "Select .bin file"
        };
        if (dlg.ShowDialog() == true)
            box.Text = dlg.FileName;
    }

    private void ReceiverCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RenderSegmentInputs();

    private void RenderSegmentInputs()
    {
        SegmentFieldsPanel.Children.Clear();
        _segmentPathBoxes.Clear();

        if (ReceiverCombo.SelectedItem is not ReceiverProfile profile)
            return;

        foreach (var seg in profile.Segments)
        {
            var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

            var label = new TextBlock
            {
                Text = seg.Required ? seg.Label : $"{seg.Label} (optional)",
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var box = new TextBox { Margin = new Thickness(0, 0, 14, 0) };
            _segmentPathBoxes[seg.Label] = box;

            var btn = new Button
            {
                Content = "…",
                Style = (Style)FindResource("IconButton")
            };
            btn.Click += (_, _) => PickInto(box);

            Grid.SetColumn(label, 0);
            Grid.SetColumn(box, 1);
            Grid.SetColumn(btn, 2);

            rowGrid.Children.Add(label);
            rowGrid.Children.Add(box);
            rowGrid.Children.Add(btn);

            SegmentFieldsPanel.Children.Add(rowGrid);
        }
    }

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

        foreach (var (label, box) in _segmentPathBoxes)
            cfg.BinPathsByLabel[label] = box.Text.Trim();

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

        using var folderDialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to save clone (.bin files)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        _cts = new CancellationTokenSource();
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
            try { macText = await esptool.ReadMacAsync(_cts.Token); } catch { /* ignore */ }

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

            var result = await esptool.ReadCloneAsync(safeFolder, _cts.Token);
            SetProgress(100);
            SetStatus("Clone saved", isSuccess: true);
            Log(result);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Read stopped");
            SetProgress(0);
            Log("Read operation canceled.");
        }
        catch (Exception ex)
        {
            SetStatus("Read error", isError: true);
            SetProgress(0);
            Log("ERROR: " + ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
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
