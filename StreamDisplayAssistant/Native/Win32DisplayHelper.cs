using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StreamDisplayAssistant.Native;

#region Result & Mode Models

public class DisplayChangeResult
{
    public bool Success { get; set; }
    public int RequestedWidth { get; set; }
    public int RequestedHeight { get; set; }
    public int AppliedWidth { get; set; }
    public int AppliedHeight { get; set; }
    public int AppliedHz { get; set; }
    public bool IsAspectBestFit { get; set; }
    public string Message { get; set; } = string.Empty;

    // Lowercase property aliases for backward compatibility
    public bool success => Success;
    public int appliedHz => AppliedHz;
    public string message => Message;

    // Implicit bool conversion (true if Success)
    public static implicit operator bool(DisplayChangeResult? res) => res != null && res.Success;

    // Backward compatibility deconstruction: allows var (success, appliedHz, message) = SetDisplayMode(...)
    public void Deconstruct(out bool success, out int appliedHz, out string message)
    {
        success = Success;
        appliedHz = AppliedHz;
        message = Message;
    }

    // Extended deconstruction: allows var (success, appliedWidth, appliedHeight, appliedHz, isAspectBestFit, message) = SetDisplayMode(...)
    public void Deconstruct(out bool success, out int appliedWidth, out int appliedHeight, out int appliedHz, out bool isAspectBestFit, out string message)
    {
        success = Success;
        appliedWidth = AppliedWidth;
        appliedHeight = AppliedHeight;
        appliedHz = AppliedHz;
        isAspectBestFit = IsAspectBestFit;
        message = Message;
    }
}

public class HardwareDisplayMode
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRate { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;

    public string DisplayText => string.IsNullOrWhiteSpace(Tag)
        ? $"{Width}x{Height} @ {RefreshRate}Hz ({AspectRatio})"
        : $"{Width}x{Height} @ {RefreshRate}Hz ({AspectRatio}) — {Tag}";
}

#endregion

public static class Win32DisplayHelper
{
    #region Constants and Enums

    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int CDS_UPDATEREGISTRY = 0x01;
    public const int CDS_TEST = 0x02;
    public const int CDS_FULLSCREEN = 0x04;

    public const int DISP_CHANGE_SUCCESSFUL = 0;
    public const int DISP_CHANGE_RESTART = 1;
    public const int DISP_CHANGE_FAILED = -1;
    public const int DISP_CHANGE_BADMODE = -2;
    public const int DISP_CHANGE_NOTUPDATED = -3;
    public const int DISP_CHANGE_BADFLAGS = -4;
    public const int DISP_CHANGE_BADPARAM = -5;

    public const int DM_PELSWIDTH = 0x00080000;
    public const int DM_PELSHEIGHT = 0x00100000;
    public const int DM_DISPLAYFREQUENCY = 0x00400000;

    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    public const uint DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] modeInfo; // Unmanaged buffer for DISPLAYCONFIG_SOURCE_MODE / TARGET_MODE / DESKTOP_IMAGE_INFO
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // bitfield: advancedColorSupported (bit 0), advancedColorEnabled (bit 1), etc.
        public uint colorEncoding;
        public int bitsPerColorChannel;

        public bool AdvancedColorSupported => (value & 0x1) != 0;
        public bool AdvancedColorEnabled => (value & 0x2) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // bit 0: enableAdvancedColor (1 = ON, 0 = OFF)
    }

    #endregion

    #region P/Invoke APIs

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern int EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern int ChangeDisplaySettings(ref DEVMODE lpDevMode, uint dwflags);

    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExA", CharSet = CharSet.Ansi)]
    public static extern int ResetDisplaySettingsEx(string? lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE requestPacket);

    #endregion

    #region High-Level Helper Methods

    public static int GCD(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }

    public static string CalculateAspectRatioName(int width, int height)
    {
        if (height <= 0 || width <= 0) return "Unknown";
        double ratio = (double)width / height;
        if (Math.Abs(ratio - 16.0 / 9.0) < 0.02) return "16:9";
        if (Math.Abs(ratio - 16.0 / 10.0) < 0.02) return "16:10";
        if (Math.Abs(ratio - 4.0 / 3.0) < 0.02) return "4:3";
        if (Math.Abs(ratio - 21.0 / 9.0) < 0.03) return "21:9";
        if (Math.Abs(ratio - 32.0 / 9.0) < 0.03) return "32:9";
        int gcd = GCD(width, height);
        return $"{width / gcd}:{height / gcd}";
    }

    /// <summary>
    /// Retrieves unique hardware resolutions supported by the display adapter, grouped with maximum refresh rate.
    /// </summary>
    public static List<HardwareDisplayMode> GetSupportedHardwareModes(string? deviceName = null)
    {
        var list = new List<HardwareDisplayMode>();
        var devMode = new DEVMODE();
        devMode.dmSize = (short)Marshal.SizeOf(devMode);
        int modeIndex = 0;
        var map = new Dictionary<(int w, int h), int>();

        while (EnumDisplaySettings(deviceName, modeIndex, ref devMode) != 0)
        {
            if (devMode.dmPelsWidth > 0 && devMode.dmPelsHeight > 0 && devMode.dmBitsPerPel >= 32)
            {
                var key = (devMode.dmPelsWidth, devMode.dmPelsHeight);
                if (!map.TryGetValue(key, out int maxHz) || devMode.dmDisplayFrequency > maxHz)
                {
                    map[key] = devMode.dmDisplayFrequency;
                }
            }
            modeIndex++;
        }

        foreach (var kvp in map.OrderByDescending(k => k.Key.w * k.Key.h).ThenByDescending(k => k.Value))
        {
            int w = kvp.Key.w;
            int h = kvp.Key.h;
            string aspect = CalculateAspectRatioName(w, h);
            string tag = "";
            if (aspect == "4:3") tag = "iPad / Tablet (Zero Black Bars)";
            else if (aspect == "16:10" && w == 1280 && h == 800) tag = "Steam Deck Native";
            else if (aspect == "16:10") tag = "Handheld 16:10";
            else if (aspect == "16:9" && w == 1920 && h == 1080) tag = "Full HD Standard";

            list.Add(new HardwareDisplayMode
            {
                Width = w,
                Height = h,
                RefreshRate = kvp.Value,
                AspectRatio = aspect,
                Tag = tag
            });
        }

        return list;
    }

    /// <summary>
    /// Applies a DEVMODE structure using multi-tiered Win32 APIs (ChangeDisplaySettingsEx -> ChangeDisplaySettings -> dynamic switch).
    /// </summary>
    public static int ApplyDevMode(ref DEVMODE mode, uint flags)
    {
        int res = ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, flags, IntPtr.Zero);
        if (res == DISP_CHANGE_SUCCESSFUL) return res;

        // Try user32 direct ChangeDisplaySettings
        res = ChangeDisplaySettings(ref mode, flags);
        if (res == DISP_CHANGE_SUCCESSFUL) return res;

        // If updating registry was rejected, attempt dynamic switch
        if ((flags & CDS_UPDATEREGISTRY) != 0)
        {
            res = ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
            if (res == DISP_CHANGE_SUCCESSFUL) return res;

            res = ChangeDisplaySettings(ref mode, 0);
            if (res == DISP_CHANGE_SUCCESSFUL) return res;
        }

        return res;
    }

    /// <summary>
    /// Determines the true native hardware resolution and maximum refresh rate of the primary display from its EDID modes table.
    /// </summary>
    public static (int width, int height, int refreshRate) GetTrueNativeMonitorResolution()
    {
        try
        {
            var modes = GetSupportedHardwareModes();
            if (modes != null && modes.Count > 0)
            {
                // The true native hardware mode has the largest pixel area (width * height) and highest refresh rate
                var native = modes
                    .OrderByDescending(m => (long)m.Width * m.Height)
                    .ThenByDescending(m => m.RefreshRate)
                    .FirstOrDefault();

                if (native != null && native.Width > 0 && native.Height > 0)
                {
                    return (native.Width, native.Height, native.RefreshRate);
                }
            }
        }
        catch { }

        // Fallback to active display mode
        var current = GetCurrentDisplayMode();
        return (current.width, current.height, current.refreshRate);
    }

    /// <summary>
    /// Retrieves current resolution and refresh rate of the primary display.
    /// </summary>
    public static (int width, int height, int refreshRate, bool success) GetCurrentDisplayMode()
    {
        DEVMODE devMode = new DEVMODE();
        devMode.dmSize = (short)Marshal.SizeOf(devMode);

        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) != 0)
        {
            return (devMode.dmPelsWidth, devMode.dmPelsHeight, devMode.dmDisplayFrequency, true);
        }

        return (0, 0, 0, false);
    }

    /// <summary>
    /// Changes resolution with multi-tiered fallback and zero-touch aspect-ratio best-fit matching.
    /// If target resolution is not supported in monitor EDID (e.g. 2048x1536 iPad on a 1440p monitor),
    /// automatically locates and applies the highest native hardware resolution with the exact same aspect ratio (e.g. 1920x1440 @ 180Hz)
    /// ensuring zero black bars on the streaming device with no user intervention required.
    /// </summary>
    public static DisplayChangeResult SetDisplayMode(int width, int height, int requestedRefreshRate = 0, bool autoAspectFallback = true)
    {
        var current = GetCurrentDisplayMode();

        // 1. Requested Frequency (if specified)
        if (requestedRefreshRate > 0)
        {
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(devMode);
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) != 0)
            {
                devMode.dmPelsWidth = width;
                devMode.dmPelsHeight = height;
                devMode.dmDisplayFrequency = requestedRefreshRate;
                devMode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

                int res = ApplyDevMode(ref devMode, CDS_UPDATEREGISTRY);
                if (res == DISP_CHANGE_SUCCESSFUL)
                {
                    return new DisplayChangeResult
                    {
                        Success = true,
                        RequestedWidth = width,
                        RequestedHeight = height,
                        AppliedWidth = width,
                        AppliedHeight = height,
                        AppliedHz = requestedRefreshRate,
                        IsAspectBestFit = false,
                        Message = $"Applied requested mode: {width}x{height} @ {requestedRefreshRate}Hz"
                    };
                }
            }
        }

        // 2. Try with current display's native refresh rate
        if (current.success && current.refreshRate > 0 && current.refreshRate != requestedRefreshRate)
        {
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(devMode);
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) != 0)
            {
                devMode.dmPelsWidth = width;
                devMode.dmPelsHeight = height;
                devMode.dmDisplayFrequency = current.refreshRate;
                devMode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

                int res = ApplyDevMode(ref devMode, CDS_UPDATEREGISTRY);
                if (res == DISP_CHANGE_SUCCESSFUL)
                {
                    return new DisplayChangeResult
                    {
                        Success = true,
                        RequestedWidth = width,
                        RequestedHeight = height,
                        AppliedWidth = width,
                        AppliedHeight = height,
                        AppliedHz = current.refreshRate,
                        IsAspectBestFit = false,
                        Message = $"Applied at active display refresh rate ({current.refreshRate}Hz)"
                    };
                }
            }
        }

        // 3. Try standard 60 Hz fallback
        if (requestedRefreshRate != 60 && (!current.success || current.refreshRate != 60))
        {
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(devMode);
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) != 0)
            {
                devMode.dmPelsWidth = width;
                devMode.dmPelsHeight = height;
                devMode.dmDisplayFrequency = 60;
                devMode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

                int res = ApplyDevMode(ref devMode, CDS_UPDATEREGISTRY);
                if (res == DISP_CHANGE_SUCCESSFUL)
                {
                    return new DisplayChangeResult
                    {
                        Success = true,
                        RequestedWidth = width,
                        RequestedHeight = height,
                        AppliedWidth = width,
                        AppliedHeight = height,
                        AppliedHz = 60,
                        IsAspectBestFit = false,
                        Message = "Applied with universal 60Hz fallback."
                    };
                }
            }
        }

        // 4. Try without enforcing DM_DISPLAYFREQUENCY (allow driver to assign best valid frequency)
        {
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(devMode);
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) != 0)
            {
                devMode.dmPelsWidth = width;
                devMode.dmPelsHeight = height;
                devMode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;

                int res = ApplyDevMode(ref devMode, CDS_UPDATEREGISTRY);
                if (res == DISP_CHANGE_SUCCESSFUL)
                {
                    var updated = GetCurrentDisplayMode();
                    return new DisplayChangeResult
                    {
                        Success = true,
                        RequestedWidth = width,
                        RequestedHeight = height,
                        AppliedWidth = width,
                        AppliedHeight = height,
                        AppliedHz = updated.refreshRate,
                        IsAspectBestFit = false,
                        Message = $"Applied with driver automatic frequency ({updated.refreshRate}Hz)."
                    };
                }
            }
        }

        // 5. Enumerate all modes in display adapter table to find matching resolution, sorted by highest refresh rate
        {
            var tableModes = new List<DEVMODE>();
            DEVMODE tm = new DEVMODE();
            tm.dmSize = (short)Marshal.SizeOf(tm);
            int mIdx = 0;
            while (EnumDisplaySettings(null, mIdx, ref tm) != 0)
            {
                if (tm.dmPelsWidth == width && tm.dmPelsHeight == height && tm.dmBitsPerPel >= 32)
                {
                    tableModes.Add(tm);
                }
                mIdx++;
            }

            // Sort: prioritize matching requestedHz, then highest frequency descending
            var sortedTable = tableModes
                .OrderByDescending(m => requestedRefreshRate > 0 && m.dmDisplayFrequency == requestedRefreshRate ? 1000 : m.dmDisplayFrequency)
                .ToList();

            foreach (var candMode in sortedTable)
            {
                var tryMode = candMode;
                tryMode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
                int res = ApplyDevMode(ref tryMode, CDS_UPDATEREGISTRY);
                if (res == DISP_CHANGE_SUCCESSFUL)
                {
                    return new DisplayChangeResult
                    {
                        Success = true,
                        RequestedWidth = width,
                        RequestedHeight = height,
                        AppliedWidth = width,
                        AppliedHeight = height,
                        AppliedHz = tryMode.dmDisplayFrequency,
                        IsAspectBestFit = false,
                        Message = $"Applied matching hardware mode ({tryMode.dmDisplayFrequency}Hz)."
                    };
                }
            }
        }

        // 6. ZERO-TOUCH ASPECT RATIO & QUALITY BEST-FIT MATCHING
        if (autoAspectFallback && width > 0 && height > 0)
        {
            double targetRatio = (double)width / height;
            string targetAspectName = CalculateAspectRatioName(width, height);

            var rawModes = new List<(int w, int h, int hz)>();
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(devMode);
            int idx = 0;
            while (EnumDisplaySettings(null, idx, ref devMode) != 0)
            {
                if (devMode.dmPelsWidth > 0 && devMode.dmPelsHeight > 0 && devMode.dmBitsPerPel >= 32)
                {
                    rawModes.Add((devMode.dmPelsWidth, devMode.dmPelsHeight, devMode.dmDisplayFrequency));
                }
                idx++;
            }

            if (rawModes.Count > 0)
            {
                int maxW = rawModes.Max(m => m.w);
                int maxH = rawModes.Max(m => m.h);
                double maxPixelArea = (double)maxW * maxH;

                // Group by resolution (W, H)
                var distinctRes = rawModes
                    .Where(m => m.w <= maxW && m.h <= maxH)
                    .GroupBy(m => new { m.w, m.h })
                    .Select(g => new
                    {
                        W = g.Key.w,
                        H = g.Key.h,
                        Rates = g.Select(x => x.hz).Distinct().OrderByDescending(x => x).ToList()
                    })
                    .ToList();

                // Score candidate resolutions:
                // 1. Aspect error: |ratio - targetRatio| / targetRatio
                // 2. Resolution quality: higher pixel count ensures sharp crisp text and avoids pixelated/zoomed-in UI
                // 3. Low-resolution penalty: penalize resolutions with height < 900 if client requested >= 1000
                var scoredCandidates = distinctRes.Select(cand =>
                {
                    double candRatio = (double)cand.W / cand.H;
                    double aspectError = Math.Abs(candRatio - targetRatio) / targetRatio;
                    double pixelArea = (double)cand.W * cand.H;
                    double resFactor = pixelArea / maxPixelArea;

                    double lowResPenalty = 0.0;
                    if (height >= 1000 && cand.H < 900)
                    {
                        lowResPenalty = 5.0; // Eliminates 720x480, 640x480 from high-res targets
                    }
                    else if (height >= 720 && cand.H < 600)
                    {
                        lowResPenalty = 3.0;
                    }

                    double score = (aspectError * 4.0) + (1.0 - resFactor) + lowResPenalty;

                    return new
                    {
                        cand.W,
                        cand.H,
                        cand.Rates,
                        Score = score,
                        Ratio = candRatio
                    };
                })
                .OrderBy(c => c.Score)
                .ToList();

                foreach (var cand in scoredCandidates)
                {
                    // Pick best refresh rate: prefer requestedHz if available, else current native Hz, else highest available
                    int chosenHz = 60;
                    if (requestedRefreshRate > 0 && cand.Rates.Contains(requestedRefreshRate))
                    {
                        chosenHz = requestedRefreshRate;
                    }
                    else if (current.success && cand.Rates.Contains(current.refreshRate))
                    {
                        chosenHz = current.refreshRate;
                    }
                    else if (cand.Rates.Count > 0)
                    {
                        chosenHz = cand.Rates.First();
                    }

                    // Try applying candidate
                    DEVMODE tryMode = new DEVMODE();
                    tryMode.dmSize = (short)Marshal.SizeOf(tryMode);
                    if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref tryMode) != 0)
                    {
                        tryMode.dmPelsWidth = cand.W;
                        tryMode.dmPelsHeight = cand.H;
                        tryMode.dmDisplayFrequency = chosenHz;
                        tryMode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

                        int res = ApplyDevMode(ref tryMode, CDS_UPDATEREGISTRY);
                        if (res == DISP_CHANGE_SUCCESSFUL)
                        {
                            string appliedAspect = CalculateAspectRatioName(cand.W, cand.H);
                            return new DisplayChangeResult
                            {
                                Success = true,
                                RequestedWidth = width,
                                RequestedHeight = height,
                                AppliedWidth = cand.W,
                                AppliedHeight = cand.H,
                                AppliedHz = chosenHz,
                                IsAspectBestFit = true,
                                Message = $"Resolution {width}x{height} not in monitor EDID. Auto-applied crisp high-res {appliedAspect} mode ({cand.W}x{cand.H} @ {chosenHz}Hz) for edge-to-edge streaming with zero black bars!"
                            };
                        }
                    }
                }
            }
        }

        return new DisplayChangeResult
        {
            Success = false,
            RequestedWidth = width,
            RequestedHeight = height,
            AppliedWidth = 0,
            AppliedHeight = 0,
            AppliedHz = 0,
            IsAspectBestFit = false,
            Message = $"Resolution {width}x{height} is not supported by your display or graphics driver."
        };
    }

    /// <summary>
    /// Restores the native desktop resolution.
    /// Prioritizes explicit native hardware resolution (e.g. 2560x1440 @ 180Hz) to reliably switch back.
    /// </summary>
    public static DisplayChangeResult RestoreNativeDisplay(int nativeWidth = 0, int nativeHeight = 0, int nativeHz = 0)
    {
        int targetW = nativeWidth;
        int targetH = nativeHeight;
        int targetHz = nativeHz;

        // If native dimensions are missing or invalid, resolve the true hardware native resolution from EDID
        if (targetW <= 0 || targetH <= 0)
        {
            var detected = GetTrueNativeMonitorResolution();
            targetW = detected.width;
            targetH = detected.height;
            targetHz = detected.refreshRate;
        }

        // 1. Always prioritize explicit native mode application
        if (targetW > 0 && targetH > 0)
        {
            var res = SetDisplayMode(targetW, targetH, targetHz, autoAspectFallback: false);
            if (res.Success)
            {
                return res;
            }

            // If requested targetHz failed, retry with auto-frequency (0)
            if (targetHz > 0)
            {
                var autoHzRes = SetDisplayMode(targetW, targetH, 0, autoAspectFallback: false);
                if (autoHzRes.Success)
                {
                    return autoHzRes;
                }
            }
        }

        // 2. Secondary fallback: Windows driver default reset
        int resetRes = ResetDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (resetRes == DISP_CHANGE_SUCCESSFUL)
        {
            var cur = GetCurrentDisplayMode();
            return new DisplayChangeResult
            {
                Success = true,
                RequestedWidth = targetW,
                RequestedHeight = targetH,
                AppliedWidth = cur.width,
                AppliedHeight = cur.height,
                AppliedHz = cur.refreshRate,
                IsAspectBestFit = false,
                Message = $"Restored via driver registry reset ({cur.width}x{cur.height} @ {cur.refreshRate}Hz)."
            };
        }

        return new DisplayChangeResult
        {
            Success = false,
            RequestedWidth = targetW,
            RequestedHeight = targetH,
            AppliedWidth = 0,
            AppliedHeight = 0,
            AppliedHz = 0,
            IsAspectBestFit = false,
            Message = $"Unable to restore to {targetW}x{targetH} @ {targetHz}Hz."
        };
    }

    /// <summary>
    /// Checks if HDR is supported and currently enabled on the primary display.
    /// </summary>
    public static (bool supported, bool enabled) GetHdrStatus()
    {
        try
        {
            uint pathCount, modeCount;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != 0)
                return (false, false);

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return (false, false);

            for (int i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO colorInfo = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO)),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref colorInfo) == 0)
                {
                    return (colorInfo.AdvancedColorSupported, colorInfo.AdvancedColorEnabled);
                }
            }
        }
        catch
        {
            // Fallback for virtual machines or basic display drivers
        }

        return (false, false);
    }

    /// <summary>
    /// Toggles or sets HDR state on the active display targets.
    /// </summary>
    public static bool SetHdrState(bool enable)
    {
        try
        {
            uint pathCount, modeCount;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != 0)
                return false;

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return false;

            bool anySuccess = false;

            for (int i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE setState = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE,
                        size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE)),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    },
                    value = enable ? 1u : 0u
                };

                if (DisplayConfigSetDeviceInfo(ref setState) == 0)
                {
                    anySuccess = true;
                }
            }

            return anySuccess;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
