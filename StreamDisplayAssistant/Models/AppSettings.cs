using System;
using System.Collections.Generic;

namespace StreamDisplayAssistant.Models;

public class AppSettings
{
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public string CustomProductId { get; set; } = "4guRWGiUi-Bkg5--OFjGnw==";

    // Saved Native Resolution
    public int NativeWidth { get; set; } = 0;
    public int NativeHeight { get; set; } = 0;
    public int NativeRefreshRate { get; set; } = 0;

    // Automation Settings
    public bool IsAutomationEnabled { get; set; } = false;
    public string TargetProcessName { get; set; } = "sunshine";
    public string CustomProcessName { get; set; } = "";
    public string TargetProfileDisplayText { get; set; } = "";
    public bool AutoEnableHdr { get; set; } = true;
    public bool AutoEnableOledBlackout { get; set; } = true;
    public bool AutoRestoreOnExit { get; set; } = true;
    public bool AutoDetectClientResolution { get; set; } = true;
    public bool AutoMuteHostAudio { get; set; } = true;
    public bool EnableGlobalHotkeys { get; set; } = true;
    public bool PlaySoundFeedback { get; set; } = true;

    // OLED Burn-in Protection Analytics & Eco Metrics
    public int TotalStreamingSessions { get; set; } = 0;
    public double TotalBlackoutSeconds { get; set; } = 0;
    public double TotalKwhSaved { get; set; } = 0;

    // Discovered Executables and Logs across Drives
    public string DetectedSunshineExePath { get; set; } = "";
    public string DetectedSunshineLogPath { get; set; } = "";
    public string DetectedMoonlightExePath { get; set; } = "";

    // User-Defined Custom Profiles
    public List<DisplayProfile> CustomProfiles { get; set; } = new();
}
