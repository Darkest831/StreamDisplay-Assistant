using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace StreamDisplayAssistant.Services;

public class DiscoveredApp
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string? LogPath { get; set; }
    public string Icon { get; set; } = "🎮";
    public string[] ProcessNames { get; set; } = Array.Empty<string>();
    public bool IsInstalled => !string.IsNullOrEmpty(ExePath) && File.Exists(ExePath);
    public bool IsRunning { get; set; }
    public string StatusText => IsRunning ? "● Running Now" : (IsInstalled ? "✔ Installed" : "Not Found");
    public string DisplayText => $"{Icon} {Name} ({Category}) - {StatusText}";
}

public class StreamingInstallationInfo
{
    public string? SunshineExePath { get; set; }
    public string? SunshineLogPath { get; set; }
    public string? SunshineConfigDir { get; set; }
    public string? MoonlightExePath { get; set; }
    public string? SteamExePath { get; set; }
    public string? ParsecExePath { get; set; }
    public string? AnyDeskExePath { get; set; }
    public string? RustDeskExePath { get; set; }
    public string? TeamViewerExePath { get; set; }
    public string? ChiakiExePath { get; set; }
    public string? ObsExePath { get; set; }

    public bool IsSunshineInstalled => !string.IsNullOrEmpty(SunshineExePath) && File.Exists(SunshineExePath);
    public bool IsSunshineLogAvailable => !string.IsNullOrEmpty(SunshineLogPath) && File.Exists(SunshineLogPath);
    public bool IsMoonlightInstalled => !string.IsNullOrEmpty(MoonlightExePath) && File.Exists(MoonlightExePath);
    public bool IsSteamInstalled => !string.IsNullOrEmpty(SteamExePath) && File.Exists(SteamExePath);
    public bool IsParsecInstalled => !string.IsNullOrEmpty(ParsecExePath) && File.Exists(ParsecExePath);

    public List<DiscoveredApp> Apps { get; set; } = new();
    public string ScanSummary { get; set; } = string.Empty;
}

/// <summary>
/// Comprehensive discovery engine that scans all local system drives, Windows registry,
/// running processes, and services for all popular game streaming and remote desktop software.
/// </summary>
public static class StreamingDiscoveryService
{
    public static StreamingInstallationInfo DiscoverInstallations()
    {
        var result = new StreamingInstallationInfo();
        var checkedDrives = new List<string>();

        // Initialize target app registry
        var appList = new List<DiscoveredApp>
        {
            new() { Id = "sunshine", Name = "Sunshine", Category = "GameStream Host", Icon = "☀️", ProcessNames = new[] { "sunshine", "sunshinesvc" } },
            new() { Id = "moonlight", Name = "Moonlight", Category = "GameStream Client", Icon = "🌙", ProcessNames = new[] { "Moonlight" } },
            new() { Id = "steam", Name = "Steam Remote Play", Category = "Remote Play / In-Home", Icon = "🎮", ProcessNames = new[] { "steam", "streaming_client" } },
            new() { Id = "parsec", Name = "Parsec", Category = "Low-Latency Remote", Icon = "⚡", ProcessNames = new[] { "parsecd", "parsec", "pservice" } },
            new() { Id = "chiaki", Name = "Chiaki / Chiaki-ng", Category = "PS Remote Play", Icon = "🕹️", ProcessNames = new[] { "chiaki", "chiaki-ng" } },
            new() { Id = "anydesk", Name = "AnyDesk", Category = "Remote Desktop", Icon = "🔴", ProcessNames = new[] { "AnyDesk" } },
            new() { Id = "rustdesk", Name = "RustDesk", Category = "Remote Desktop", Icon = "🦀", ProcessNames = new[] { "rustdesk" } },
            new() { Id = "teamviewer", Name = "TeamViewer", Category = "Remote Desktop", Icon = "🌐", ProcessNames = new[] { "TeamViewer" } },
            new() { Id = "obs", Name = "OBS Studio", Category = "Broadcasting / Capture", Icon = "📹", ProcessNames = new[] { "obs64", "obs32" } },
            new() { Id = "nvstream", Name = "NVIDIA GameStream", Category = "Shield Host", Icon = "🟢", ProcessNames = new[] { "nvstreamer", "nvstreamsvc" } }
        };

        // 1. Check Running Processes (Instant and highly reliable for running instances)
        foreach (var app in appList)
        {
            foreach (var procName in app.ProcessNames)
            {
                try
                {
                    var procs = Process.GetProcessesByName(procName);
                    if (procs.Length > 0)
                    {
                        app.IsRunning = true;
                        try
                        {
                            string? mainPath = procs[0].MainModule?.FileName;
                            if (!string.IsNullOrEmpty(mainPath) && File.Exists(mainPath) && string.IsNullOrEmpty(app.ExePath))
                            {
                                app.ExePath = mainPath;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // 2. Check Windows Services (SunshineService, Parsec, AnyDesk, TeamViewer)
        try
        {
            CheckServiceExecutable(@"SYSTEM\CurrentControlSet\Services\SunshineService", p =>
            {
                string dir = Path.GetDirectoryName(p) ?? string.Empty;
                string parentDir = Directory.GetParent(dir)?.FullName ?? dir;
                string possibleExe = Path.Combine(parentDir, "sunshine.exe");
                if (File.Exists(possibleExe))
                {
                    result.SunshineExePath = possibleExe;
                    result.SunshineConfigDir = Path.Combine(parentDir, "config");
                    string possibleLog = Path.Combine(parentDir, "config", "sunshine.log");
                    if (File.Exists(possibleLog)) result.SunshineLogPath = possibleLog;
                }
            });

            CheckServiceExecutable(@"SYSTEM\CurrentControlSet\Services\Parsec", p =>
            {
                if (File.Exists(p)) result.ParsecExePath = p;
            });

            CheckServiceExecutable(@"SYSTEM\CurrentControlSet\Services\AnyDesk", p =>
            {
                if (File.Exists(p)) result.AnyDeskExePath = p;
            });
        }
        catch { }

        // 3. Scan Windows Registry Uninstall entries (HKLM + HKCU, 32-bit and 64-bit)
        try
        {
            string[] registryRoots = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var root in registryRoots)
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey(root);
                if (hklmKey != null) InspectRegistryUninstallKeys(hklmKey, appList, result);

                using var hkcuKey = Registry.CurrentUser.OpenSubKey(root);
                if (hkcuKey != null) InspectRegistryUninstallKeys(hkcuKey, appList, result);
            }
        }
        catch { }

        // 4. Scan all ready drives for standard paths across C:, D:, E:, F:, etc.
        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
            foreach (var drive in drives)
            {
                string root = drive.Name;
                checkedDrives.Add(root.TrimEnd('\\'));

                // Standard paths map
                var standardChecks = new (string AppId, string RelPath)[]
                {
                    ("sunshine", @"Sunshine\sunshine.exe"),
                    ("sunshine", @"Program Files\Sunshine\sunshine.exe"),
                    ("sunshine", @"Program Files (x86)\Sunshine\sunshine.exe"),
                    ("sunshine", @"Tools\Sunshine\sunshine.exe"),
                    ("moonlight", @"Program Files\Moonlight Game Streaming\Moonlight.exe"),
                    ("moonlight", @"Program Files (x86)\Moonlight Game Streaming\Moonlight.exe"),
                    ("steam", @"Program Files (x86)\Steam\steam.exe"),
                    ("steam", @"Program Files\Steam\steam.exe"),
                    ("steam", @"Steam\steam.exe"),
                    ("parsec", @"Program Files\Parsec\parsecd.exe"),
                    ("anydesk", @"Program Files (x86)\AnyDesk\AnyDesk.exe"),
                    ("anydesk", @"Program Files\AnyDesk\AnyDesk.exe"),
                    ("rustdesk", @"Program Files\RustDesk\rustdesk.exe"),
                    ("teamviewer", @"Program Files\TeamViewer\TeamViewer.exe"),
                    ("teamviewer", @"Program Files (x86)\TeamViewer\TeamViewer.exe"),
                    ("obs", @"Program Files\obs-studio\bin\64bit\obs64.exe"),
                    ("obs", @"Program Files (x86)\obs-studio\bin\32bit\obs32.exe"),
                    ("chiaki", @"Chiaki\chiaki.exe"),
                    ("chiaki", @"Program Files\Chiaki\chiaki.exe")
                };

                foreach (var (appId, relPath) in standardChecks)
                {
                    var targetApp = appList.FirstOrDefault(a => a.Id == appId);
                    if (targetApp != null && string.IsNullOrEmpty(targetApp.ExePath))
                    {
                        string full = Path.Combine(root, relPath);
                        if (File.Exists(full))
                        {
                            targetApp.ExePath = full;
                            if (appId == "sunshine")
                            {
                                string dir = Path.GetDirectoryName(full) ?? string.Empty;
                                result.SunshineConfigDir = Path.Combine(dir, "config");
                                string possibleLog = Path.Combine(dir, "config", "sunshine.log");
                                if (File.Exists(possibleLog)) result.SunshineLogPath = possibleLog;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 5. Check user AppData for user-level installs (e.g. Parsec, AnyDesk, Sunshine Log)
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            // Sunshine log search
            if (string.IsNullOrEmpty(result.SunshineLogPath))
            {
                string[] possibleLogPaths = new[]
                {
                    Path.Combine(programData, "Sunshine", "sunshine.log"),
                    Path.Combine(programData, "Sunshine", "config", "sunshine.log"),
                    Path.Combine(localAppData, "Sunshine", "sunshine.log"),
                    Path.Combine(appData, "Sunshine", "sunshine.log")
                };
                foreach (var lp in possibleLogPaths)
                {
                    if (File.Exists(lp)) { result.SunshineLogPath = lp; break; }
                }
            }

            // Parsec user install
            var parsec = appList.First(a => a.Id == "parsec");
            if (string.IsNullOrEmpty(parsec.ExePath))
            {
                string candP = Path.Combine(appData, "Parsec", "parsecd.exe");
                if (File.Exists(candP)) parsec.ExePath = candP;
            }

            // AnyDesk user install
            var anydesk = appList.First(a => a.Id == "anydesk");
            if (string.IsNullOrEmpty(anydesk.ExePath))
            {
                string candA = Path.Combine(appData, "AnyDesk", "AnyDesk.exe");
                if (File.Exists(candA)) anydesk.ExePath = candA;
            }
        }
        catch { }

        // 6. Map back specific properties
        var sunshineApp = appList.FirstOrDefault(a => a.Id == "sunshine");
        if (sunshineApp != null && !string.IsNullOrEmpty(sunshineApp.ExePath))
        {
            result.SunshineExePath = sunshineApp.ExePath;
            if (string.IsNullOrEmpty(result.SunshineLogPath))
            {
                string dir = Path.GetDirectoryName(sunshineApp.ExePath) ?? string.Empty;
                string possibleLog = Path.Combine(dir, "config", "sunshine.log");
                if (File.Exists(possibleLog)) result.SunshineLogPath = possibleLog;
            }
        }
        else if (!string.IsNullOrEmpty(result.SunshineExePath) && sunshineApp != null)
        {
            sunshineApp.ExePath = result.SunshineExePath;
        }

        var moonlightApp = appList.FirstOrDefault(a => a.Id == "moonlight");
        if (moonlightApp != null && !string.IsNullOrEmpty(moonlightApp.ExePath))
        {
            result.MoonlightExePath = moonlightApp.ExePath;
        }

        var steamApp = appList.FirstOrDefault(a => a.Id == "steam");
        if (steamApp != null && !string.IsNullOrEmpty(steamApp.ExePath))
        {
            result.SteamExePath = steamApp.ExePath;
        }

        var parsecApp = appList.FirstOrDefault(a => a.Id == "parsec");
        if (parsecApp != null && !string.IsNullOrEmpty(parsecApp.ExePath))
        {
            result.ParsecExePath = parsecApp.ExePath;
        }

        var anydeskApp = appList.FirstOrDefault(a => a.Id == "anydesk");
        if (anydeskApp != null && !string.IsNullOrEmpty(anydeskApp.ExePath)) result.AnyDeskExePath = anydeskApp.ExePath;

        var rustdeskApp = appList.FirstOrDefault(a => a.Id == "rustdesk");
        if (rustdeskApp != null && !string.IsNullOrEmpty(rustdeskApp.ExePath)) result.RustDeskExePath = rustdeskApp.ExePath;

        var teamviewerApp = appList.FirstOrDefault(a => a.Id == "teamviewer");
        if (teamviewerApp != null && !string.IsNullOrEmpty(teamviewerApp.ExePath)) result.TeamViewerExePath = teamviewerApp.ExePath;

        var chiakiApp = appList.FirstOrDefault(a => a.Id == "chiaki");
        if (chiakiApp != null && !string.IsNullOrEmpty(chiakiApp.ExePath)) result.ChiakiExePath = chiakiApp.ExePath;

        var obsApp = appList.FirstOrDefault(a => a.Id == "obs");
        if (obsApp != null && !string.IsNullOrEmpty(obsApp.ExePath)) result.ObsExePath = obsApp.ExePath;

        result.Apps = appList;

        // Build Scan Summary
        int installedCount = appList.Count(a => a.IsInstalled);
        int runningCount = appList.Count(a => a.IsRunning);
        string installedNames = string.Join(", ", appList.Where(a => a.IsInstalled).Select(a => a.Name));

        result.ScanSummary = $"Scanned drives [{string.Join(", ", checkedDrives)}]: Found {installedCount} streaming app(s) ({installedNames}). {runningCount} currently running.";

        return result;
    }

    private static void CheckServiceExecutable(string registryServicePath, Action<string> onFound)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(registryServicePath);
            if (key != null)
            {
                object? imagePathObj = key.GetValue("ImagePath");
                if (imagePathObj is string imagePath && !string.IsNullOrWhiteSpace(imagePath))
                {
                    string cleanPath = imagePath.Trim('\"', ' ');
                    if (File.Exists(cleanPath))
                    {
                        onFound(cleanPath);
                    }
                }
            }
        }
        catch { }
    }

    private static void InspectRegistryUninstallKeys(RegistryKey rootKey, List<DiscoveredApp> apps, StreamingInstallationInfo result)
    {
        foreach (string subKeyName in rootKey.GetSubKeyNames())
        {
            try
            {
                using var subKey = rootKey.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                string? dispName = subKey.GetValue("DisplayName") as string;
                if (string.IsNullOrEmpty(dispName)) continue;

                string? icon = subKey.GetValue("DisplayIcon") as string;
                string? loc = subKey.GetValue("InstallLocation") as string;

                foreach (var app in apps)
                {
                    if (string.IsNullOrEmpty(app.ExePath) && dispName.IndexOf(app.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!string.IsNullOrEmpty(icon))
                        {
                            string cleanIcon = icon.Split(',')[0].Trim('\"', ' ');
                            if (File.Exists(cleanIcon) && cleanIcon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                app.ExePath = cleanIcon;
                            }
                        }
                        else if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc))
                        {
                            foreach (var procName in app.ProcessNames)
                            {
                                string cand = Path.Combine(loc, procName + ".exe");
                                if (File.Exists(cand))
                                {
                                    app.ExePath = cand;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}
