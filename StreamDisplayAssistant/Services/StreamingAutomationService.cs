using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using StreamDisplayAssistant.Models;
using StreamDisplayAssistant.Native;
using StreamDisplayAssistant.Views;

namespace StreamDisplayAssistant.Services;

public class AutomationConfig
{
    public string TargetProcessName { get; set; } = "sunshine";
    public DisplayProfile? TargetProfile { get; set; }
    public bool AutoEnableHdr { get; set; } = true;
    public bool AutoEnableOledBlackout { get; set; } = true;
    public bool AutoRestoreOnExit { get; set; } = true;
    public bool AutoDetectClientResolution { get; set; } = true;
    public bool AutoMuteHostAudio { get; set; } = true;
    public bool PlaySoundFeedback { get; set; } = true;
    public bool IsEnabled { get; set; } = false;

    // Discovered paths across drives
    public string SunshineLogPath { get; set; } = string.Empty;
    public string SunshineExePath { get; set; } = string.Empty;
}

public class StreamingAutomationService
{
    private readonly GumroadLicenseService _licenseService;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private bool _isStreamingActive;
    private SunshineSessionMonitor? _sunshineMonitor;

    // Native state saved prior to streaming
    private int _originalWidth;
    private int _originalHeight;
    private int _originalRefreshRate;
    private bool _originalHdrEnabled;

    private OledBlackoutWindow? _activeBlackoutWindow;
    private DateTime? _sessionStartTime;

    public AutomationConfig Config { get; set; } = new();

    public event Action<bool, string>? AutomationStateChanged;
    public event Action<string>? LogMessage;
    public event Action<double>? SessionStatsRecorded;

    public StreamingAutomationService(GumroadLicenseService licenseService)
    {
        _licenseService = licenseService;
    }

    public void StartMonitoring()
    {
        if (_monitorTask != null && !_monitorTask.IsCompleted)
            return;

        _cts = new CancellationTokenSource();

        // 1. If targeting Sunshine (or Any Streaming App), launch high-precision real-time log monitor
        if (Config.TargetProcessName.Equals("sunshine", StringComparison.OrdinalIgnoreCase) ||
            Config.TargetProcessName.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            EnsureSunshinePathsDiscovered();

            if (!string.IsNullOrEmpty(Config.SunshineLogPath) && File.Exists(Config.SunshineLogPath))
            {
                _sunshineMonitor?.Stop();
                _sunshineMonitor = new SunshineSessionMonitor(Config.SunshineLogPath);
                _sunshineMonitor.SessionStarted += OnSunshineSessionStarted;
                _sunshineMonitor.SessionEnded += OnSunshineSessionEnded;
                _sunshineMonitor.LogMessage += msg => LogMessage?.Invoke(msg);
                _sunshineMonitor.Start();
                LogMessage?.Invoke($"Sunshine session engine linked to log: {Config.SunshineLogPath}");
            }
            else
            {
                LogMessage?.Invoke("Warning: Sunshine log file not located yet. Process monitor will run as fallback.");
            }
        }

        // 2. Launch process supervisor loop
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        LogMessage?.Invoke("Streaming automation service active.");
    }

    public void StopMonitoring()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        if (_sunshineMonitor != null)
        {
            _sunshineMonitor.Stop();
            _sunshineMonitor = null;
        }

        if (_isStreamingActive)
        {
            RestoreDisplaySettings();
        }

        LogMessage?.Invoke("Streaming automation service stopped.");
    }

    public void EnsureSunshinePathsDiscovered()
    {
        if (string.IsNullOrEmpty(Config.SunshineLogPath) || !File.Exists(Config.SunshineLogPath) ||
            string.IsNullOrEmpty(Config.SunshineExePath) || !File.Exists(Config.SunshineExePath))
        {
            var disco = StreamingDiscoveryService.DiscoverInstallations();
            if (disco.IsSunshineInstalled)
            {
                Config.SunshineExePath = disco.SunshineExePath!;
            }
            if (disco.IsSunshineLogAvailable)
            {
                Config.SunshineLogPath = disco.SunshineLogPath!;
            }
            LogMessage?.Invoke(disco.ScanSummary);
        }
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (Config.IsEnabled && _licenseService.IsProLicensed)
                {
                    // For non-Sunshine targets (e.g. moonlight PC client or custom game executable)
                    if (!Config.TargetProcessName.Equals("sunshine", StringComparison.OrdinalIgnoreCase))
                    {
                        bool processRunning = CheckProcessRunning(Config.TargetProcessName);

                        if (processRunning && !_isStreamingActive)
                        {
                            _isStreamingActive = true;
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ApplyStreamingSettings());
                        }
                        else if (!processRunning && _isStreamingActive)
                        {
                            _isStreamingActive = false;
                            if (Config.AutoRestoreOnExit)
                            {
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(RestoreDisplaySettings);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Monitoring alert: {ex.Message}");
            }

            try
            {
                await Task.Delay(2000, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private void OnSunshineSessionStarted(StreamSessionStartedEventArgs e)
    {
        if (!Config.IsEnabled || !_licenseService.IsProLicensed)
            return;

        if (_isStreamingActive)
            return;

        _isStreamingActive = true;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            ApplyStreamingSettings(e.ClientWidth, e.ClientHeight, e.ClientFps, e.DeviceDescription);
        });
    }

    private void OnSunshineSessionEnded()
    {
        if (!Config.IsEnabled || !_licenseService.IsProLicensed)
            return;

        if (!_isStreamingActive)
            return;

        _isStreamingActive = false;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (Config.AutoRestoreOnExit)
            {
                RestoreDisplaySettings();
            }
        });
    }

    private bool CheckProcessRunning(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        if (processName.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            string[] anyCandidates = new[] { "streaming_client", "parsecd", "parsec", "Moonlight", "AnyDesk", "rustdesk", "TeamViewer", "chiaki", "chiaki-ng", "obs64" };
            return anyCandidates.Any(c => Process.GetProcessesByName(c).Length > 0);
        }

        string cleanName = Path.GetFileNameWithoutExtension(processName.Trim());
        Process[] processes = Process.GetProcessesByName(cleanName);
        return processes.Length > 0;
    }

    private void ApplyStreamingSettings(int clientWidth = 0, int clientHeight = 0, int clientFps = 0, string deviceDesc = "")
    {
        _sessionStartTime = DateTime.UtcNow;

        // 1. Lock true native monitor hardware resolution from EDID
        var nativeHard = Win32DisplayHelper.GetTrueNativeMonitorResolution();
        _originalWidth = nativeHard.width;
        _originalHeight = nativeHard.height;
        _originalRefreshRate = nativeHard.refreshRate;

        var hdr = Win32DisplayHelper.GetHdrStatus();
        _originalHdrEnabled = hdr.enabled;

        LogMessage?.Invoke($"Saving native state: {_originalWidth}x{_originalHeight}@{_originalRefreshRate}Hz, HDR: {_originalHdrEnabled}");

        // 2. Determine target resolution to apply
        bool useAutoDetect = Config.AutoDetectClientResolution || 
                             Config.TargetProfile == null || 
                             Config.TargetProfile.IsAutoDetect;

        if (useAutoDetect && clientWidth > 0 && clientHeight > 0)
        {
            LogMessage?.Invoke($"⚡ [AUTO-ADAPT] Client connected: {deviceDesc} requesting {clientWidth}x{clientHeight} @ {clientFps}Hz.");

            var res = Win32DisplayHelper.SetDisplayMode(clientWidth, clientHeight, clientFps, autoAspectFallback: true);

            string statusMsg = res.IsAspectBestFit
                ? $"Auto-adapted to {res.AppliedWidth}x{res.AppliedHeight} @ {res.AppliedHz}Hz (Zero Black Bars for {deviceDesc})"
                : $"Applied exact client mode: {res.AppliedWidth}x{res.AppliedHeight} @ {res.AppliedHz}Hz";

            LogMessage?.Invoke($"Resolution engine: {res.Message} -> {statusMsg}");
            AutomationStateChanged?.Invoke(true, $"Streaming Active: {deviceDesc} -> {res.AppliedWidth}x{res.AppliedHeight}");
        }
        else if (Config.TargetProfile != null && !Config.TargetProfile.IsAutoDetect)
        {
            var res = Win32DisplayHelper.SetDisplayMode(
                Config.TargetProfile.Width,
                Config.TargetProfile.Height,
                Config.TargetProfile.RefreshRate,
                autoAspectFallback: true);

            LogMessage?.Invoke($"Switching resolution to {Config.TargetProfile.DisplayText}: {res.Message}");
            AutomationStateChanged?.Invoke(true, $"Streaming Active: {Config.TargetProfile.DisplayText}");
        }
        else
        {
            // Default 1080p fallback if no client resolution detected yet
            var res = Win32DisplayHelper.SetDisplayMode(1920, 1080, 60, autoAspectFallback: true);
            LogMessage?.Invoke($"Default profile applied: {res.Message}");
            AutomationStateChanged?.Invoke(true, "Streaming Active: 1920x1080 @ 60Hz");
        }

        // 3. Enable HDR if requested
        if (Config.AutoEnableHdr)
        {
            bool hdrOk = Win32DisplayHelper.SetHdrState(true);
            LogMessage?.Invoke($"Automatic HDR enable: {(hdrOk ? "Success" : "Not supported / already on")}");
        }

        // 4. Auto-Mute Host Speakers if enabled
        if (Config.AutoMuteHostAudio)
        {
            bool muteOk = AudioControlService.MuteHostAudio();
            LogMessage?.Invoke($"Host PC audio auto-mute: {(muteOk ? "Speakers muted for remote session" : "Mute not available")}");
        }

        // 5. Sound Chime feedback
        if (Config.PlaySoundFeedback)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
        }

        // 6. Enable OLED Blackout if requested
        if (Config.AutoEnableOledBlackout)
        {
            if (_activeBlackoutWindow == null || !_activeBlackoutWindow.IsLoaded)
            {
                _activeBlackoutWindow = new OledBlackoutWindow();
                _activeBlackoutWindow.Closed += (s, e) => _activeBlackoutWindow = null;
                _activeBlackoutWindow.Show();
                LogMessage?.Invoke("OLED Blackout automatically activated on host screen.");
            }
        }
    }

    public void RestoreDisplaySettings()
    {
        LogMessage?.Invoke("Restoring native display configuration...");

        // Record session duration for OLED burn-in metrics
        if (_sessionStartTime.HasValue)
        {
            var elapsed = DateTime.UtcNow - _sessionStartTime.Value;
            _sessionStartTime = null;
            SessionStatsRecorded?.Invoke(elapsed.TotalSeconds);
        }

        // 1. Close OLED Blackout window if active
        if (_activeBlackoutWindow != null)
        {
            try
            {
                _activeBlackoutWindow.Close();
            }
            catch { }
            _activeBlackoutWindow = null;
        }

        // 2. Restore Host Speakers audio
        if (Config.AutoMuteHostAudio)
        {
            AudioControlService.RestoreHostAudio();
            LogMessage?.Invoke("Host PC speakers un-muted (audio restored).");
        }

        // 3. Sound Chime feedback
        if (Config.PlaySoundFeedback)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
        }

        // 4. Restore HDR state
        if (Config.AutoEnableHdr && !_originalHdrEnabled)
        {
            Win32DisplayHelper.SetHdrState(false);
            LogMessage?.Invoke("HDR restored to original state (OFF).");
        }

        // 5. Restore true native monitor resolution
        bool resOk = Win32DisplayHelper.RestoreNativeDisplay(_originalWidth, _originalHeight, _originalRefreshRate);
        LogMessage?.Invoke($"Native resolution restore: {(resOk ? "Success" : "Failed")}");

        AutomationStateChanged?.Invoke(false, $"Native desktop restored ({_originalWidth}x{_originalHeight} @ {_originalRefreshRate}Hz).");
    }

    public void ToggleBlackout()
    {
        if (_activeBlackoutWindow != null)
        {
            try { _activeBlackoutWindow.Close(); } catch { }
            _activeBlackoutWindow = null;
            LogMessage?.Invoke("OLED Blackout dismissed.");
        }
        else
        {
            _activeBlackoutWindow = new OledBlackoutWindow();
            _activeBlackoutWindow.Closed += (s, e) => _activeBlackoutWindow = null;
            _activeBlackoutWindow.Show();
            LogMessage?.Invoke("OLED Blackout shield activated.");
        }
    }
}
