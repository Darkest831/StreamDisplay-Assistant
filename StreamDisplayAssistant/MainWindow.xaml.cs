using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using StreamDisplayAssistant.Models;
using StreamDisplayAssistant.Native;
using StreamDisplayAssistant.Services;
using StreamDisplayAssistant.Views;
using Forms = System.Windows.Forms;

namespace StreamDisplayAssistant;

public partial class MainWindow : Window
{
    private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "StreamDisplayAssistant";

    private readonly GumroadLicenseService _licenseService;
    private readonly StreamingAutomationService _automationService;
    private readonly GlobalHotkeyService _hotkeyService = new();
    private readonly string _settingsFilePath;
    private AppSettings _settings = new();

    private Forms.NotifyIcon? _notifyIcon;
    private int _savedNativeWidth = 0;
    private int _savedNativeHeight = 0;
    private int _savedNativeRefreshRate = 0;
    private List<DisplayProfile> _allProfiles = new();
    private List<HardwareDisplayMode> _hardwareModes = new();
    private bool _isExplicitExit = false;

    public MainWindow(bool startMinimized = false)
    {
        App.LogTrace("MainWindow constructor started.");
        InitializeComponent();
        App.LogTrace("InitializeComponent completed.");

        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamDisplayAssistant");
        if (!Directory.Exists(appData))
        {
            Directory.CreateDirectory(appData);
        }
        _settingsFilePath = Path.Combine(appData, "settings.json");

        _licenseService = new GumroadLicenseService();
        _automationService = new StreamingAutomationService(_licenseService);

        App.LogTrace("Loading settings...");
        LoadSettings();
        App.LogTrace("Initializing display profiles...");
        InitializeDisplayProfiles();
        App.LogTrace("Capturing initial display...");
        CaptureInitialNativeDisplay();
        App.LogTrace("Initializing license UI...");
        InitializeLicenseUi();
        App.LogTrace("Initializing tray icon...");
        InitializeTrayIcon();

        _licenseService.LicenseStatusChanged += OnLicenseStatusChanged;
        _automationService.AutomationStateChanged += OnAutomationStateChanged;
        _automationService.LogMessage += AppendLog;
        _automationService.SessionStatsRecorded += OnSessionStatsRecorded;

        // Global Hotkey event handlers
        _hotkeyService.OnEmergencyRestoreNativeRequested += () => Dispatcher.Invoke(() => BtnRestoreNative_Click(this, new RoutedEventArgs()));
        _hotkeyService.OnToggleBlackoutRequested += () => Dispatcher.Invoke(() => QuickOledBlackout_Click(this, new RoutedEventArgs()));
        _hotkeyService.OnToggleHdrRequested += () => Dispatcher.Invoke(ToggleHdr_Click);
        _hotkeyService.OnToggleMuteRequested += () => AudioControlService.ToggleMute();
        _hotkeyService.LogMessage += AppendLog;

        ChkAutoDetectClientRes.Checked += ChkAutoDetectClientRes_Changed;
        ChkAutoDetectClientRes.Unchecked += ChkAutoDetectClientRes_Changed;
        CmbAutoProfiles.SelectionChanged += CmbAutoProfiles_SelectionChanged;

        ChkAutoMuteHostAudio.Checked += (s, e) => { UpdateAutomationConfig(); SaveSettings(); };
        ChkAutoMuteHostAudio.Unchecked += (s, e) => { UpdateAutomationConfig(); SaveSettings(); };
        ChkSoundFeedback.Checked += (s, e) => { UpdateAutomationConfig(); SaveSettings(); };
        ChkSoundFeedback.Unchecked += (s, e) => { UpdateAutomationConfig(); SaveSettings(); };

        Loaded += (s, e) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            App.LogTrace($"MainWindow Loaded successfully! HWND: 0x{hwnd.ToInt64():X}");
            if (_settings.EnableGlobalHotkeys)
            {
                _hotkeyService.RegisterHotkeys(hwnd);
            }
        };
        ContentRendered += (s, e) => App.LogTrace("MainWindow ContentRendered visible on desktop!");

        App.LogTrace("Launching background drive scan...");
        ScanAllDrivesForStreamingApps();

        if (startMinimized)
        {
            App.LogTrace("Starting minimized to tray.");
            WindowState = WindowState.Minimized;
            Hide();
            if (_notifyIcon != null && _settings.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(2500, "StreamDisplay Assistant", "Started with Windows. Running quietly in background.", Forms.ToolTipIcon.Info);
            }
        }
        App.LogTrace("MainWindow constructor completed.");
    }

    #region Settings Persistence & Autostart

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Settings load notice: {ex.Message}");
        }

        // Apply settings to UI controls
        ChkStartWithWindows.IsChecked = CheckRegistryAutoStart();
        ChkMinimizeToTrayOnClose.IsChecked = _settings.MinimizeToTrayOnClose;
        ChkShowNotifications.IsChecked = _settings.ShowNotifications;

        if (!string.IsNullOrWhiteSpace(_settings.CustomProductId))
        {
            TxtCustomProductId.Text = _settings.CustomProductId;
            _licenseService.ProductId = _settings.CustomProductId;
        }

        ChkAutoHdr.IsChecked = _settings.AutoEnableHdr;
        ChkAutoBlackout.IsChecked = _settings.AutoEnableOledBlackout;
        ChkAutoRestore.IsChecked = _settings.AutoRestoreOnExit;
        ChkAutoDetectClientRes.IsChecked = _settings.AutoDetectClientResolution;
        ChkAutoMuteHostAudio.IsChecked = _settings.AutoMuteHostAudio;
        ChkSoundFeedback.IsChecked = _settings.PlaySoundFeedback;
        ChkEnableGlobalHotkeys.IsChecked = _settings.EnableGlobalHotkeys;
        UpdateStatsUi();

        if (!string.IsNullOrEmpty(_settings.DetectedSunshineExePath))
        {
            TxtDetectedSunshinePath.Text = $"✔ Found Sunshine: {_settings.DetectedSunshineExePath}";
            TxtDetectedSunshinePath.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));
        }

        // Restore Process Target
        string targetLower = _settings.TargetProcessName?.ToLowerInvariant() ?? "any";
        switch (targetLower)
        {
            case "any": CmbTargetProcess.SelectedIndex = 0; break;
            case "sunshine": CmbTargetProcess.SelectedIndex = 1; break;
            case "steam": CmbTargetProcess.SelectedIndex = 2; break;
            case "parsec": CmbTargetProcess.SelectedIndex = 3; break;
            case "moonlight": CmbTargetProcess.SelectedIndex = 4; break;
            case "anydesk": CmbTargetProcess.SelectedIndex = 5; break;
            case "rustdesk": CmbTargetProcess.SelectedIndex = 6; break;
            case "teamviewer": CmbTargetProcess.SelectedIndex = 7; break;
            case "chiaki": CmbTargetProcess.SelectedIndex = 8; break;
            case "obs64": CmbTargetProcess.SelectedIndex = 9; break;
            default:
                CmbTargetProcess.SelectedIndex = 10;
                TxtCustomProcess.Text = _settings.TargetProcessName;
                TxtCustomProcess.Visibility = Visibility.Visible;
                break;
        }

        // Restore Automation if user had it enabled and has PRO license
        if (_settings.IsAutomationEnabled && _licenseService.IsProLicensed)
        {
            ChkEnableAutomation.IsChecked = true;
            UpdateAutomationConfig();
            _automationService.StartMonitoring();
            TxtStatusBar.Text = $"Background monitor active for '{_automationService.Config.TargetProcessName}'...";
            AppendLog($"Automation resumed automatically for {_automationService.Config.TargetProcessName}.");
            UpdateAutomationBannerState(true);
        }
        else
        {
            UpdateAutomationConfig();
            UpdateAutomationBannerState(false);
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settings.MinimizeToTrayOnClose = ChkMinimizeToTrayOnClose.IsChecked == true;
            _settings.ShowNotifications = ChkShowNotifications.IsChecked == true;
            _settings.CustomProductId = TxtCustomProductId.Text;
            _settings.IsAutomationEnabled = ChkEnableAutomation.IsChecked == true;
            _settings.AutoEnableHdr = ChkAutoHdr.IsChecked == true;
            _settings.AutoEnableOledBlackout = ChkAutoBlackout.IsChecked == true;
            _settings.AutoRestoreOnExit = ChkAutoRestore.IsChecked == true;
            _settings.AutoDetectClientResolution = ChkAutoDetectClientRes.IsChecked == true;
            _settings.AutoMuteHostAudio = ChkAutoMuteHostAudio.IsChecked == true;
            _settings.PlaySoundFeedback = ChkSoundFeedback.IsChecked == true;
            _settings.EnableGlobalHotkeys = ChkEnableGlobalHotkeys.IsChecked == true;

            if (_savedNativeWidth > 0 && _savedNativeHeight > 0)
            {
                _settings.NativeWidth = _savedNativeWidth;
                _settings.NativeHeight = _savedNativeHeight;
                _settings.NativeRefreshRate = _savedNativeRefreshRate;
            }

            int sel = CmbTargetProcess.SelectedIndex;
            switch (sel)
            {
                case 0: _settings.TargetProcessName = "any"; break;
                case 1: _settings.TargetProcessName = "sunshine"; break;
                case 2: _settings.TargetProcessName = "steam"; break;
                case 3: _settings.TargetProcessName = "parsec"; break;
                case 4: _settings.TargetProcessName = "moonlight"; break;
                case 5: _settings.TargetProcessName = "anydesk"; break;
                case 6: _settings.TargetProcessName = "rustdesk"; break;
                case 7: _settings.TargetProcessName = "teamviewer"; break;
                case 8: _settings.TargetProcessName = "chiaki"; break;
                case 9: _settings.TargetProcessName = "obs64"; break;
                case 10: _settings.TargetProcessName = TxtCustomProcess.Text.Trim(); break;
                default: _settings.TargetProcessName = "any"; break;
            }

            if (CmbAutoProfiles.SelectedItem is DisplayProfile prof)
            {
                _settings.TargetProfileDisplayText = prof.DisplayText;
            }

            string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            AppendLog($"Settings save notice: {ex.Message}");
        }
    }

    private bool CheckRegistryAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppRegistryName) != null;
        }
        catch
        {
            return false;
        }
    }

    private void SetRegistryAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key != null)
            {
                if (enable)
                {
                    string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    key.SetValue(AppRegistryName, $"\"{appPath}\" --autostart");
                    AppendLog("Auto-start with Windows enabled (starts minimized to tray).");
                }
                else
                {
                    key.DeleteValue(AppRegistryName, false);
                    AppendLog("Auto-start with Windows disabled.");
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Unable to modify startup registry: {ex.Message}", "Registry Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #endregion

    #region Display Profiles & Native Display Tracking

    private void InitializeDisplayProfiles()
    {
        _allProfiles = DisplayProfile.GetDefaultProfiles();

        // Inject saved custom profiles at top of list
        if (_settings.CustomProfiles != null && _settings.CustomProfiles.Count > 0)
        {
            foreach (var custom in _settings.CustomProfiles)
            {
                if (!_allProfiles.Any(p => p.Width == custom.Width && p.Height == custom.Height && p.RefreshRate == custom.RefreshRate))
                {
                    _allProfiles.Insert(0, custom);
                }
            }
        }

        var manualProfiles = _allProfiles.Where(p => !p.IsAutoDetect).ToList();
        CmbDisplayProfiles.ItemsSource = null;
        CmbDisplayProfiles.ItemsSource = manualProfiles;
        CmbDisplayProfiles.DisplayMemberPath = "DisplayText";
        if (manualProfiles.Count > 0)
            CmbDisplayProfiles.SelectedIndex = 0;

        CmbAutoProfiles.ItemsSource = null;
        CmbAutoProfiles.ItemsSource = _allProfiles;
        CmbAutoProfiles.DisplayMemberPath = "DisplayText";

        // Restore target profile selection if saved
        if (!string.IsNullOrWhiteSpace(_settings.TargetProfileDisplayText))
        {
            var match = _allProfiles.FirstOrDefault(p => p.DisplayText == _settings.TargetProfileDisplayText);
            if (match != null)
            {
                CmbAutoProfiles.SelectedItem = match;
            }
            else if (_allProfiles.Count > 0)
            {
                CmbAutoProfiles.SelectedIndex = 0;
            }
        }
        else if (_allProfiles.Count > 0)
        {
            CmbAutoProfiles.SelectedIndex = 0;
        }

        InitializeHardwareModes();
    }

    private void InitializeHardwareModes()
    {
        try
        {
            _hardwareModes = Win32DisplayHelper.GetSupportedHardwareModes();
            CmbHardwareModes.ItemsSource = null;
            CmbHardwareModes.ItemsSource = _hardwareModes;
            CmbHardwareModes.DisplayMemberPath = "DisplayText";
            if (_hardwareModes.Count > 0)
            {
                CmbHardwareModes.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Hardware modes query note: {ex.Message}");
        }
    }

    private void CaptureInitialNativeDisplay()
    {
        var trueNative = Win32DisplayHelper.GetTrueNativeMonitorResolution();
        var current = Win32DisplayHelper.GetCurrentDisplayMode();

        // Always lock in the TRUE native resolution of the physical monitor hardware (never overwritten by temporary modes)
        if (trueNative.width > 0 && trueNative.height > 0)
        {
            _savedNativeWidth = trueNative.width;
            _savedNativeHeight = trueNative.height;
            _savedNativeRefreshRate = trueNative.refreshRate;
        }
        else if (_settings.NativeWidth > 0 && _settings.NativeHeight > 0)
        {
            _savedNativeWidth = _settings.NativeWidth;
            _savedNativeHeight = _settings.NativeHeight;
            _savedNativeRefreshRate = _settings.NativeRefreshRate;
        }
        else if (current.success)
        {
            _savedNativeWidth = current.width;
            _savedNativeHeight = current.height;
            _savedNativeRefreshRate = current.refreshRate;
        }

        _settings.NativeWidth = _savedNativeWidth;
        _settings.NativeHeight = _savedNativeHeight;
        _settings.NativeRefreshRate = _savedNativeRefreshRate;
        SaveSettings();

        TxtNativeSaved.Text = $"{_savedNativeWidth} x {_savedNativeHeight} @ {_savedNativeRefreshRate} Hz";
        if (current.success)
        {
            TxtCurrentResolution.Text = $"{current.width} x {current.height} @ {current.refreshRate} Hz";
            TxtStatusBar.Text = $"Native display recorded: {_savedNativeWidth}x{_savedNativeHeight} @ {_savedNativeRefreshRate}Hz";
        }
        else
        {
            TxtCurrentResolution.Text = "Not detected";
            TxtStatusBar.Text = "Unable to query display via EnumDisplaySettings.";
        }

        UpdateHdrStatusUi();
    }

    private void UpdateCurrentDisplayStatusUi()
    {
        var current = Win32DisplayHelper.GetCurrentDisplayMode();
        if (current.success)
        {
            TxtCurrentResolution.Text = $"{current.width} x {current.height} @ {current.refreshRate} Hz";
        }
        else
        {
            TxtCurrentResolution.Text = "Not detected";
        }

        // TxtNativeSaved is permanently kept untouched!
        TxtNativeSaved.Text = $"{_savedNativeWidth} x {_savedNativeHeight} @ {_savedNativeRefreshRate} Hz";
        UpdateHdrStatusUi();
    }

    private void UpdateHdrStatusUi()
    {
        var hdr = Win32DisplayHelper.GetHdrStatus();
        if (!hdr.supported)
        {
            TxtCurrentHdr.Text = "Not supported";
            TxtCurrentHdr.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
        }
        else if (hdr.enabled)
        {
            TxtCurrentHdr.Text = "Active (ON)";
            TxtCurrentHdr.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
        }
        else
        {
            TxtCurrentHdr.Text = "Disabled (OFF)";
            TxtCurrentHdr.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
        }
    }

    private void InitializeLicenseUi()
    {
        UpdateLicenseVisuals(_licenseService.IsProLicensed);
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "StreamDisplay Assistant"
        };

        _notifyIcon.DoubleClick += (s, e) => RestoreFromTray();

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("🖥️ Open StreamDisplay Assistant", null, (s, e) => RestoreFromTray());
        contextMenu.Items.Add("🛡️ Instant OLED Blackout", null, (s, e) => QuickOledBlackout_Click(this, new RoutedEventArgs()));
        contextMenu.Items.Add("🌈 Toggle HDR", null, (s, e) => ToggleHdr_Click());
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("❌ Exit", null, (s, e) => ExplicitExit());

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    #endregion

    #region Gumroad License Management

    private void OnLicenseStatusChanged(bool isPro)
    {
        Dispatcher.Invoke(() => UpdateLicenseVisuals(isPro));
    }

    private void UpdateLicenseVisuals(bool isPro)
    {
        if (isPro)
        {
            LicenseBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
            TxtLicenseBadge.Text = "👑 PRO EDITION";
            TxtLicenseBadge.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0));

            ProNoticeBanner.Visibility = Visibility.Collapsed;

            TxtStatusIcon.Text = "👑";
            TxtLicenseTitle.Text = "PRO License Active";
            TxtLicenseTitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));

            string email = _licenseService.CurrentLicense?.Email ?? "Active License";
            string date = _licenseService.CurrentLicense?.PurchaseDate ?? "Recent";
            TxtLicenseDetail.Text = $"Licensed to: {email} • Activated: {date} • Zero-touch streaming automation unlocked.";

            BtnDeactivateLicense.Visibility = Visibility.Visible;
            TxtLicenseKeyInput.IsEnabled = false;
            BtnVerifyLicense.IsEnabled = false;

            TxtStatusBar.Text = "License state: PRO Active. All automation features unlocked.";
            AppendLog("PRO License verified and active.");
        }
        else
        {
            LicenseBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85));
            TxtLicenseBadge.Text = "FREE EDITION";
            TxtLicenseBadge.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));

            ProNoticeBanner.Visibility = Visibility.Visible;

            TxtStatusIcon.Text = "🔓";
            TxtLicenseTitle.Text = "Free Edition";
            TxtLicenseTitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
            TxtLicenseDetail.Text = "Core manual display controls are active. To unlock background Sunshine/Moonlight automation, enter your license key.";

            BtnDeactivateLicense.Visibility = Visibility.Collapsed;
            TxtLicenseKeyInput.IsEnabled = true;
            BtnVerifyLicense.IsEnabled = true;

            ChkEnableAutomation.IsChecked = false;
            TxtStatusBar.Text = "Free Edition active. Manual control mode available.";
        }
    }

    private async void BtnVerifyLicense_Click(object sender, RoutedEventArgs e)
    {
        string key = TxtLicenseKeyInput.Text;
        if (string.IsNullOrWhiteSpace(key))
        {
            System.Windows.MessageBox.Show("Please enter your license key before proceeding.", "Missing Key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnVerifyLicense.IsEnabled = false;
        BtnVerifyLicense.Content = "⏳ Verifying with Gumroad...";
        TxtStatusBar.Text = "Contacting Gumroad verification servers...";

        string customPid = TxtCustomProductId.Text;
        var result = await _licenseService.VerifyAndActivateLicenseAsync(key, customPid);

        BtnVerifyLicense.IsEnabled = true;
        BtnVerifyLicense.Content = "🔑 Verify & Activate PRO";

        if (result.success)
        {
            System.Windows.MessageBox.Show(result.message, "Activation Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtStatusBar.Text = "License successfully activated!";
            SaveSettings();
        }
        else
        {
            System.Windows.MessageBox.Show(result.message, "Activation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatusBar.Text = "License verification failed.";
        }
    }

    private void BtnBuyLicense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string url = "https://gumroad.com";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Unable to open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDeactivateLicense_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show("Are you sure you want to deactivate your license from this PC?", "Confirm Deactivation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            _licenseService.DeactivateLicense();
            TxtLicenseKeyInput.Text = "";
            SaveSettings();
            System.Windows.MessageBox.Show("License removed successfully. Application reverted to Free Edition.", "License Deactivated", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnGoToLicenseTab_Click(object sender, RoutedEventArgs e)
    {
        TabLicense.IsSelected = true;
    }

    #endregion

    #region Manual Display Controls (Free Tier) & Custom Resolutions

    private void BtnRefreshDisplayStatus_Click(object sender, RoutedEventArgs e)
    {
        UpdateCurrentDisplayStatusUi();
        AppendLog("Display status refreshed.");
    }

    private void BtnApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (CmbDisplayProfiles.SelectedItem is DisplayProfile selected)
        {
            var res = Win32DisplayHelper.SetDisplayMode(selected.Width, selected.Height, selected.RefreshRate);
            if (res.Success)
            {
                if (res.IsAspectBestFit)
                {
                    TxtStatusBar.Text = $"✨ Auto-matched to native {res.AppliedWidth}x{res.AppliedHeight} @ {res.AppliedHz}Hz (0 black bars on {selected.Name})";
                    AppendLog($"Zero-Touch Auto-Fit for '{selected.Name}': {res.Message}");
                }
                else
                {
                    TxtStatusBar.Text = $"Resolution switched to: {selected.Name} ({selected.Width}x{selected.Height} @ {res.AppliedHz}Hz)";
                    AppendLog($"Resolution applied: {selected.Name} ({selected.Width}x{selected.Height} @ {res.AppliedHz}Hz). {res.Message}");
                }
                UpdateCurrentDisplayStatusUi();
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Unable to apply {selected.DisplayText}.\n\nReason: {res.Message}\n\nYou can also select a guaranteed mode from 'Monitor Hardware Native Modes' above.",
                    "Resolution Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void BtnApplyHardwareMode_Click(object sender, RoutedEventArgs e)
    {
        if (CmbHardwareModes.SelectedItem is HardwareDisplayMode mode)
        {
            var res = Win32DisplayHelper.SetDisplayMode(mode.Width, mode.Height, mode.RefreshRate);
            if (res.Success)
            {
                TxtStatusBar.Text = $"Hardware mode applied: {mode.Width}x{mode.Height} @ {res.AppliedHz}Hz ({mode.AspectRatio})";
                AppendLog($"Direct hardware mode applied: {mode.DisplayText}");
                UpdateCurrentDisplayStatusUi();
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Unable to apply hardware mode {mode.DisplayText}.\n\nReason: {res.Message}",
                    "Hardware Mode Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void BtnRestoreNative_Click(object sender, RoutedEventArgs e)
    {
        var res = Win32DisplayHelper.RestoreNativeDisplay(_savedNativeWidth, _savedNativeHeight, _savedNativeRefreshRate);
        if (res.Success)
        {
            TxtStatusBar.Text = $"Native resolution restored: {res.AppliedWidth}x{res.AppliedHeight} @ {res.AppliedHz}Hz";
            AppendLog($"Native resolution restored ({res.AppliedWidth}x{res.AppliedHeight}@{res.AppliedHz}Hz). {res.Message}");
            UpdateCurrentDisplayStatusUi();
        }
        else
        {
            System.Windows.MessageBox.Show(
                $"Unable to restore native resolution automatically.\n\nTried restoring to {_savedNativeWidth}x{_savedNativeHeight} @ {_savedNativeRefreshRate}Hz.\n\nReason: {res.Message}",
                "Restore Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void BtnApplyCustom_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtCustomWidth.Text.Trim(), out int width) || width <= 0)
        {
            System.Windows.MessageBox.Show("Please enter a valid positive number for Width (e.g. 1920, 2048).", "Invalid Width", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtCustomHeight.Text.Trim(), out int height) || height <= 0)
        {
            System.Windows.MessageBox.Show("Please enter a valid positive number for Height (e.g. 1080, 1536).", "Invalid Height", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int hz = 0;
        string hzStr = TxtCustomHz.Text.Trim();
        if (!hzStr.Equals("Auto", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(hzStr))
        {
            int.TryParse(hzStr, out hz);
        }

        var res = Win32DisplayHelper.SetDisplayMode(width, height, hz);
        if (res.Success)
        {
            if (res.IsAspectBestFit)
            {
                TxtStatusBar.Text = $"✨ Auto-matched {width}x{height} to native {res.AppliedWidth}x{res.AppliedHeight} @ {res.AppliedHz}Hz (Zero black bars)";
                AppendLog($"Custom {width}x{height} auto-fit: {res.Message}");
            }
            else
            {
                TxtStatusBar.Text = $"Custom resolution applied: {width}x{height} @ {res.AppliedHz}Hz";
                AppendLog($"Custom resolution applied: {width}x{height} @ {res.AppliedHz}Hz ({res.Message})");
            }
            UpdateCurrentDisplayStatusUi();
        }
        else
        {
            System.Windows.MessageBox.Show(
                $"Unable to apply custom resolution {width}x{height}.\n\nReason: {res.Message}\n\nTip: You can select a guaranteed compatible mode from 'Monitor Hardware Native Modes' above.",
                "Custom Resolution Notice",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BtnSaveCustomProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtCustomWidth.Text.Trim(), out int width) || width <= 0 ||
            !int.TryParse(TxtCustomHeight.Text.Trim(), out int height) || height <= 0)
        {
            System.Windows.MessageBox.Show("Please enter valid width and height numbers before saving.", "Invalid Values", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int hz = 60;
        string hzStr = TxtCustomHz.Text.Trim();
        if (!hzStr.Equals("Auto", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(hzStr))
        {
            int.TryParse(hzStr, out hz);
            if (hz <= 0) hz = 60;
        }

        string aspect = CalculateAspectRatio(width, height);
        string profileName = $"Custom ({width}x{height})";

        var newProfile = new DisplayProfile
        {
            Name = profileName,
            Category = "Custom",
            Width = width,
            Height = height,
            RefreshRate = hz,
            AspectRatio = aspect
        };

        if (!_settings.CustomProfiles.Any(p => p.Width == width && p.Height == height && p.RefreshRate == hz))
        {
            _settings.CustomProfiles.Add(newProfile);
            SaveSettings();
        }

        // Re-initialize profiles and select new profile
        InitializeDisplayProfiles();
        var match = _allProfiles.FirstOrDefault(p => p.Width == width && p.Height == height && p.RefreshRate == hz);
        if (match != null)
        {
            CmbDisplayProfiles.SelectedItem = match;
        }

        AppendLog($"Custom profile saved to presets: {newProfile.DisplayText}");
        System.Windows.MessageBox.Show($"Profile '{newProfile.DisplayText}' saved to your presets list!", "Profile Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string CalculateAspectRatio(int width, int height)
    {
        int gcd = GCD(width, height);
        int w = width / gcd;
        int h = height / gcd;
        if (w == 8 && h == 5) return "16:10";
        if (w == 16 && h == 9) return "16:9";
        if (w == 4 && h == 3) return "4:3";
        return $"{w}:{h}";
    }

    private int GCD(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }

    private void BtnHdrOn_Click(object sender, RoutedEventArgs e)
    {
        bool ok = Win32DisplayHelper.SetHdrState(true);
        UpdateHdrStatusUi();
        AppendLog($"HDR ON command sent. Result: {(ok ? "OK" : "Not supported / no change")}");
    }

    private void BtnHdrOff_Click(object sender, RoutedEventArgs e)
    {
        bool ok = Win32DisplayHelper.SetHdrState(false);
        UpdateHdrStatusUi();
        AppendLog($"HDR OFF command sent. Result: {(ok ? "OK" : "Not supported / no change")}");
    }

    private void ToggleHdr_Click()
    {
        var hdr = Win32DisplayHelper.GetHdrStatus();
        Win32DisplayHelper.SetHdrState(!hdr.enabled);
        UpdateHdrStatusUi();
    }

    private void QuickOledBlackout_Click(object sender, RoutedEventArgs e)
    {
        var blackout = new OledBlackoutWindow();
        blackout.Show();
        AppendLog("OLED Blackout activated manually. Press ESC, Space, or double-click to exit.");
    }

    #endregion

    #region Streaming Automation (Pro Tier)

    private void BtnToggleAutomationBig_Click(object sender, RoutedEventArgs e)
    {
        ChkEnableAutomation.IsChecked = ChkEnableAutomation.IsChecked != true;
    }

    private void UpdateAutomationBannerState(bool isEnabled)
    {
        if (BoxAutomationBanner == null || TxtAutomationBannerStatusDot == null ||
            TxtAutomationBannerTitle == null || TxtAutomationBannerDesc == null ||
            BtnToggleAutomationBig == null) return;

        if (isEnabled)
        {
            BoxAutomationBanner.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x06, 0x4E, 0x3B));
            BoxAutomationBanner.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));
            TxtAutomationBannerStatusDot.Text = "🟢";
            TxtAutomationBannerTitle.Text = "BACKGROUND AUTOMATION IS ACTIVE & LISTENING";
            TxtAutomationBannerTitle.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99));
            TxtAutomationBannerDesc.Text = "Ready for incoming streams! As soon as Moonlight, Sunshine, Steam, or Parsec starts a session, your resolution and HDR will automatically adapt, and restore when you disconnect.";
            BtnToggleAutomationBig.Content = "⏸ PAUSE AUTOMATION";
            BtnToggleAutomationBig.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x41, 0x55));
        }
        else
        {
            BoxAutomationBanner.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x1D, 0x13));
            BoxAutomationBanner.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEA, 0xB3, 0x08));
            TxtAutomationBannerStatusDot.Text = "⚠️";
            TxtAutomationBannerTitle.Text = "BACKGROUND AUTOMATION IS PAUSED";
            TxtAutomationBannerTitle.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFA, 0xCC, 0x15));
            TxtAutomationBannerDesc.Text = "Manual mode active. Turn ON Background Automation so StreamDisplay Assistant can automatically adapt your resolution and HDR when you stream from your iPad, Steam Deck, or Phone!";
            BtnToggleAutomationBig.Content = "▶ TURN ON AUTOMATION";
            BtnToggleAutomationBig.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB));
        }
    }

    private void ChkEnableAutomation_Changed(object sender, RoutedEventArgs e)
    {
        if (_licenseService == null || _automationService == null) return;

        if (ChkEnableAutomation.IsChecked == true)
        {
            if (!_licenseService.IsProLicensed)
            {
                ChkEnableAutomation.IsChecked = false;
                UpdateAutomationBannerState(false);
                var res = System.Windows.MessageBox.Show(
                    "Background zero-touch automation is an exclusive feature of the PRO Edition.\n\nWould you like to visit the License tab to activate or purchase it for €4.99?",
                    "PRO Feature Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (res == MessageBoxResult.Yes)
                {
                    TabLicense.IsSelected = true;
                }
                return;
            }

            UpdateAutomationConfig();
            _automationService.StartMonitoring();
            TxtStatusBar.Text = $"Streaming monitor active for process '{_automationService.Config.TargetProcessName}'...";
            UpdateAutomationBannerState(true);
            SaveSettings();
        }
        else
        {
            _automationService.StopMonitoring();
            TxtStatusBar.Text = "Streaming monitoring stopped.";
            UpdateAutomationBannerState(false);
            SaveSettings();
        }
    }

    private void UpdateAutomationConfig()
    {
        if (_automationService == null || _settings == null || CmbTargetProcess == null || CmbAutoProfiles == null)
            return;

        string process = "any";
        int sel = CmbTargetProcess.SelectedIndex;
        switch (sel)
        {
            case 0: process = "any"; break;
            case 1: process = "sunshine"; break;
            case 2: process = "steam"; break;
            case 3: process = "parsec"; break;
            case 4: process = "moonlight"; break;
            case 5: process = "anydesk"; break;
            case 6: process = "rustdesk"; break;
            case 7: process = "teamviewer"; break;
            case 8: process = "chiaki"; break;
            case 9: process = "obs64"; break;
            case 10: process = TxtCustomProcess?.Text ?? ""; break;
            default: process = "any"; break;
        }

        _automationService.Config.TargetProcessName = process;
        _automationService.Config.TargetProfile = CmbAutoProfiles.SelectedItem as DisplayProfile;
        _automationService.Config.AutoDetectClientResolution = (ChkAutoDetectClientRes?.IsChecked == true) || 
            (_automationService.Config.TargetProfile?.IsAutoDetect == true);
        _automationService.Config.AutoEnableHdr = ChkAutoHdr?.IsChecked == true;
        _automationService.Config.AutoEnableOledBlackout = ChkAutoBlackout?.IsChecked == true;
        _automationService.Config.AutoRestoreOnExit = ChkAutoRestore?.IsChecked == true;
        _automationService.Config.AutoMuteHostAudio = ChkAutoMuteHostAudio?.IsChecked == true;
        _automationService.Config.PlaySoundFeedback = ChkSoundFeedback?.IsChecked == true;
        _automationService.Config.IsEnabled = ChkEnableAutomation?.IsChecked == true;
        _automationService.Config.SunshineLogPath = _settings.DetectedSunshineLogPath;
        _automationService.Config.SunshineExePath = _settings.DetectedSunshineExePath;
    }

    private void ChkAutoDetectClientRes_Changed(object sender, RoutedEventArgs e)
    {
        if (_automationService == null || _settings == null) return;
        UpdateAutomationConfig();
        SaveSettings();
    }

    private void CmbAutoProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_automationService == null || _settings == null) return;
        UpdateAutomationConfig();
        SaveSettings();
    }

    private void BtnRescanDrives_Click(object sender, RoutedEventArgs e)
    {
        if (TxtDetectedSunshinePath != null)
        {
            TxtDetectedSunshinePath.Text = "🔍 Scanning all drives (C:, D:, E:, etc.)...";
            TxtDetectedSunshinePath.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8));
        }
        ScanAllDrivesForStreamingApps();
    }

    private void ScanAllDrivesForStreamingApps()
    {
        Task.Run(() =>
        {
            var info = StreamingDiscoveryService.DiscoverInstallations();
            Dispatcher.Invoke(() =>
            {
                if (info.IsSunshineInstalled)
                {
                    _settings.DetectedSunshineExePath = info.SunshineExePath ?? "";
                    _settings.DetectedSunshineLogPath = info.SunshineLogPath ?? "";
                }
                if (info.IsMoonlightInstalled) _settings.DetectedMoonlightExePath = info.MoonlightExePath ?? "";

                // Format detected apps summary & list
                var installed = info.Apps.Where(a => a.IsInstalled).ToList();
                if (TxtDetectedSunshinePath != null)
                {
                    TxtDetectedSunshinePath.Text = $"✔ Found {installed.Count} streaming app(s) across your drives. ({string.Join(", ", installed.Select(a => a.Name))})";
                    TxtDetectedSunshinePath.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));
                }

                if (TxtDetectedAppsList != null)
                {
                    if (installed.Count > 0)
                    {
                        var lines = installed.Select(a => $"{a.Icon} {a.Name}: {a.ExePath} ({(a.IsRunning ? "● Running Now" : "Ready")})");
                        TxtDetectedAppsList.Text = string.Join("\n", lines);
                    }
                    else
                    {
                        TxtDetectedAppsList.Text = "No standard streaming applications detected. You can select 'Custom Executable' above.";
                    }
                }

                UpdateAutomationConfig();
                AppendLog(info.ScanSummary);
            });
        });
    }

    private void CmbTargetProcess_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtCustomProcess == null) return;

        TxtCustomProcess.Visibility = CmbTargetProcess.SelectedIndex == 10
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_automationService != null && _settings != null)
        {
            UpdateAutomationConfig();
            SaveSettings();
        }
    }

    private void OnAutomationStateChanged(bool active, string msg)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatusBar.Text = msg;
            if (ChkShowNotifications.IsChecked == true && _notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(3000, "StreamDisplay Assistant", msg, Forms.ToolTipIcon.Info);
            }
            UpdateCurrentDisplayStatusUi();
        });
    }

    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
            TxtLogConsole.AppendText(line);
            LogScrollViewer.ScrollToEnd();
        });
    }

    #endregion

    #region Window, Tray & Windows Startup

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        if (_notifyIcon != null && ChkShowNotifications.IsChecked == true)
        {
            _notifyIcon.ShowBalloonTip(2000, "StreamDisplay Assistant", "Running quietly in the System Tray.", Forms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnSessionStatsRecorded(double seconds)
    {
        Dispatcher.Invoke(() =>
        {
            _settings.TotalStreamingSessions++;
            _settings.TotalBlackoutSeconds += seconds;
            _settings.TotalKwhSaved += (seconds / 3600.0) * 0.075;
            UpdateStatsUi();
            SaveSettings();
        });
    }

    private void UpdateStatsUi()
    {
        if (TxtStatsSessions == null || TxtStatsHours == null || TxtStatsKwh == null || TxtStatsBurnIn == null) return;
        TxtStatsSessions.Text = $"{_settings.TotalStreamingSessions} Sessions";
        double hours = _settings.TotalBlackoutSeconds / 3600.0;
        TxtStatsHours.Text = $"{hours:F1} Hours";
        TxtStatsKwh.Text = $"{_settings.TotalKwhSaved:F2} kWh";
        TxtStatsBurnIn.Text = "0.0% (Zero Risk)";
    }

    private void BtnResetStats_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            "Reset OLED protection statistics back to 0?",
            "Reset Analytics",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            _settings.TotalStreamingSessions = 0;
            _settings.TotalBlackoutSeconds = 0;
            _settings.TotalKwhSaved = 0;
            UpdateStatsUi();
            SaveSettings();
            AppendLog("OLED protection analytics reset.");
        }
    }

    private void ChkEnableGlobalHotkeys_Changed(object sender, RoutedEventArgs e)
    {
        if (_hotkeyService == null || _settings == null) return;
        bool enable = ChkEnableGlobalHotkeys.IsChecked == true;
        _settings.EnableGlobalHotkeys = enable;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (enable && hwnd != IntPtr.Zero)
        {
            _hotkeyService.RegisterHotkeys(hwnd);
        }
        else
        {
            _hotkeyService.UnregisterHotkeys();
        }
        SaveSettings();
    }

    private void ExplicitExit()
    {
        App.LogTrace("ExplicitExit invoked!");
        _isExplicitExit = true;
        _hotkeyService.UnregisterHotkeys();
        _automationService.StopMonitoring();
        SaveSettings();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        App.LogTrace($"Window_Closing triggered (isExplicitExit={_isExplicitExit}, minimizeToTray={ChkMinimizeToTrayOnClose.IsChecked})");
        if (!_isExplicitExit && ChkMinimizeToTrayOnClose.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            if (_notifyIcon != null && ChkShowNotifications.IsChecked == true)
            {
                _notifyIcon.ShowBalloonTip(2000, "StreamDisplay Assistant", "Still running in background. Double-click tray icon to open.", Forms.ToolTipIcon.Info);
            }
        }
        else
        {
            ExplicitExit();
        }
    }

    private void ChkStartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkStartWithWindows.IsChecked == true;
        SetRegistryAutoStart(enable);
        SaveSettings();
    }

    #endregion
}