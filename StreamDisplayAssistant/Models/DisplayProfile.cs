using System.Collections.Generic;

namespace StreamDisplayAssistant.Models;

public class DisplayProfile
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRate { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public bool IsAutoDetect { get; set; } = false;

    public string DisplayText => IsAutoDetect 
        ? Name 
        : $"{Name} ({Width}x{Height} @ {RefreshRate}Hz - {AspectRatio})";

    public static readonly DisplayProfile AutoDetectProfile = new()
    {
        Name = "✨ Auto-Detect from Client (iPad Air M2, Steam Deck, Custom)",
        Category = "Automatic",
        Width = 0,
        Height = 0,
        RefreshRate = 0,
        AspectRatio = "Auto",
        IsAutoDetect = true
    };

    public static List<DisplayProfile> GetDefaultProfiles()
    {
        return new List<DisplayProfile>
        {
            // Auto Detect Option
            AutoDetectProfile,

            // Tablets & iPads (Zero Black Bars)
            new() { Name = "iPad Universal (4:3 Native)", Category = "Tablet", Width = 1920, Height = 1440, RefreshRate = 180, AspectRatio = "4:3" },
            new() { Name = "iPad Air M2 (11-inch)", Category = "Tablet", Width = 2360, Height = 1640, RefreshRate = 60, AspectRatio = "14:10" },
            new() { Name = "iPad Pro / Air (4:3 Retina)", Category = "Tablet", Width = 2048, Height = 1536, RefreshRate = 120, AspectRatio = "4:3" },
            new() { Name = "iPad Standard (4:3 1080p)", Category = "Tablet", Width = 1440, Height = 1080, RefreshRate = 60, AspectRatio = "4:3" },

            // Handheld Presets
            new() { Name = "Steam Deck LCD", Category = "Handheld", Width = 1280, Height = 800, RefreshRate = 60, AspectRatio = "16:10" },
            new() { Name = "Steam Deck OLED", Category = "Handheld", Width = 1280, Height = 800, RefreshRate = 90, AspectRatio = "16:10" },
            new() { Name = "ROG Ally", Category = "Handheld", Width = 1920, Height = 1080, RefreshRate = 120, AspectRatio = "16:9" },
            new() { Name = "Legion Go / Tablet 2.5K", Category = "Handheld", Width = 2560, Height = 1600, RefreshRate = 144, AspectRatio = "16:10" },
            new() { Name = "Handheld 1200p", Category = "Handheld", Width = 1920, Height = 1200, RefreshRate = 120, AspectRatio = "16:10" },

            // Standard Monitors & TVs
            new() { Name = "Full HD 1080p (60Hz)", Category = "TV / Monitor", Width = 1920, Height = 1080, RefreshRate = 60, AspectRatio = "16:9" },
            new() { Name = "Full HD 1080p (120Hz)", Category = "TV / Monitor", Width = 1920, Height = 1080, RefreshRate = 120, AspectRatio = "16:9" },
            new() { Name = "2K 1440p (120Hz)", Category = "TV / Monitor", Width = 2560, Height = 1440, RefreshRate = 120, AspectRatio = "16:9" },
            new() { Name = "2K 1440p (180Hz)", Category = "TV / Monitor", Width = 2560, Height = 1440, RefreshRate = 180, AspectRatio = "16:9" },
            new() { Name = "4K Ultra HD (60Hz)", Category = "TV / Monitor", Width = 3840, Height = 2160, RefreshRate = 60, AspectRatio = "16:9" },
            new() { Name = "4K Ultra HD (120Hz OLED)", Category = "TV / Monitor", Width = 3840, Height = 2160, RefreshRate = 120, AspectRatio = "16:9" }
        };
    }
}
