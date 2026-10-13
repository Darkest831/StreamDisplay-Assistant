using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDisplayAssistant.Services;

public class StreamSessionStartedEventArgs : EventArgs
{
    public int ClientWidth { get; set; }
    public int ClientHeight { get; set; }
    public int ClientFps { get; set; }
    public string DeviceDescription { get; set; } = string.Empty;
}

public class SunshineSessionMonitor : IDisposable
{
    private readonly string _logPath;
    private CancellationTokenSource? _cts;
    private Task? _watcherTask;
    private bool _isSessionActive;
    private long _lastOffset;

    private int _cachedClientWidth;
    private int _cachedClientHeight;
    private int _cachedClientFps = 60;

    // Compiled Regex Patterns
    private static readonly Regex JsonResRegex = new(
        @"""resolution""\s*:\s*\{[^}]*""height""\s*:\s*(?<h>\d+)[^}]*""width""\s*:\s*(?<w>\d+)|""resolution""\s*:\s*\{[^}]*""width""\s*:\s*(?<w>\d+)[^}]*""height""\s*:\s*(?<h>\d+)",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex LineResRegex = new(
        @"(?:Desktop resolution|Resolution|CLIENT: Resolution)\s*\[?(?<w>\d+)\s*[xX]\s*(?<h>\d+)\]?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FpsRegex = new(
        @"(?:Requested frame rate|framerate|fps)\s*\[(?<fps>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ClientConnectedRegex = new(
        @"CLIENT CONNECTED|New streaming session started \[active sessions:\s*[1-9]\d*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ClientDisconnectedRegex = new(
        @"CLIENT DISCONNECTED|active sessions:\s*0|Session ended",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public event Action<StreamSessionStartedEventArgs>? SessionStarted;
    public event Action? SessionEnded;
    public event Action<string>? LogMessage;

    public bool IsSessionActive => _isSessionActive;
    public int LastClientWidth => _cachedClientWidth;
    public int LastClientHeight => _cachedClientHeight;
    public int LastClientFps => _cachedClientFps;

    public SunshineSessionMonitor(string logPath)
    {
        _logPath = logPath;
    }

    public void Start()
    {
        if (_watcherTask != null && !_watcherTask.IsCompleted)
            return;

        _cts = new CancellationTokenSource();
        _watcherTask = Task.Run(() => WatchLogLoopAsync(_cts.Token));
        LogMessage?.Invoke($"Sunshine live log monitor activated: {_logPath}");
    }

    public void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        _isSessionActive = false;
    }

    private async Task WatchLogLoopAsync(CancellationToken token)
    {
        // 1. Initial State Assessment
        if (File.Exists(_logPath))
        {
            try
            {
                using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long initialLen = fs.Length;

                // Inspect last 8KB to check if a session is currently active
                long readStart = Math.Max(0, initialLen - 8192);
                fs.Seek(readStart, SeekOrigin.Begin);
                using var initialReader = new StreamReader(fs, Encoding.UTF8, leaveOpen: true);
                string tail = await initialReader.ReadToEndAsync(token);

                InspectInitialState(tail);
                _lastOffset = initialLen;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Initial Sunshine log inspect note: {ex.Message}");
            }
        }

        // 2. Continuous Tail Polling
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(_logPath))
                {
                    using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    long currentLen = stream.Length;

                    // Log was rotated or cleared
                    if (currentLen < _lastOffset)
                    {
                        _lastOffset = 0;
                    }

                    if (currentLen > _lastOffset)
                    {
                        stream.Seek(_lastOffset, SeekOrigin.Begin);
                        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                        string newContent = await reader.ReadToEndAsync(token);
                        _lastOffset = stream.Position;

                        ProcessNewLogChunk(newContent);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Log read notification: {ex.Message}");
            }

            try
            {
                await Task.Delay(600, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private void InspectInitialState(string tail)
    {
        // Extract any recent resolution
        ExtractResolution(tail);

        // Check if currently connected
        int lastConnIdx = tail.LastIndexOf("CLIENT CONNECTED", StringComparison.OrdinalIgnoreCase);
        int lastDisconnIdx = tail.LastIndexOf("CLIENT DISCONNECTED", StringComparison.OrdinalIgnoreCase);

        if (lastConnIdx > lastDisconnIdx && !_isSessionActive)
        {
            _isSessionActive = true;
            TriggerSessionStart();
        }
    }

    private void ProcessNewLogChunk(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk)) return;

        // 1. Extract incoming resolution if present
        ExtractResolution(chunk);

        // 2. Extract incoming frame rate if present
        var fpsMatch = FpsRegex.Match(chunk);
        if (fpsMatch.Success && int.TryParse(fpsMatch.Groups["fps"].Value, out int fps) && fps > 0)
        {
            _cachedClientFps = fps;
        }

        // 3. Check for Client Disconnect
        bool hasDisconnect = ClientDisconnectedRegex.IsMatch(chunk);
        if (hasDisconnect && _isSessionActive)
        {
            _isSessionActive = false;
            LogMessage?.Invoke("Sunshine log signal: CLIENT DISCONNECTED. Stream ended.");
            SessionEnded?.Invoke();
        }

        // 4. Check for Client Connect
        bool hasConnect = ClientConnectedRegex.IsMatch(chunk);
        if (hasConnect && !_isSessionActive)
        {
            _isSessionActive = true;
            TriggerSessionStart();
        }
    }

    private void ExtractResolution(string text)
    {
        // First check JSON resolution blocks (which Sunshine prints upon client handshake)
        var jsonMatches = JsonResRegex.Matches(text);
        foreach (Match m in jsonMatches)
        {
            string wStr = m.Groups["w"].Value;
            string hStr = m.Groups["h"].Value;
            if (int.TryParse(wStr, out int w) && int.TryParse(hStr, out int h) && w > 0 && h > 0)
            {
                _cachedClientWidth = w;
                _cachedClientHeight = h;
            }
        }

        // If not in JSON, check single line resolution
        if (_cachedClientWidth == 0 || _cachedClientHeight == 0)
        {
            var lineMatches = LineResRegex.Matches(text);
            foreach (Match m in lineMatches)
            {
                string wStr = m.Groups["w"].Value;
                string hStr = m.Groups["h"].Value;
                if (int.TryParse(wStr, out int w) && int.TryParse(hStr, out int h) && w > 0 && h > 0)
                {
                    _cachedClientWidth = w;
                    _cachedClientHeight = h;
                }
            }
        }
    }

    private void TriggerSessionStart()
    {
        string device = GuessDeviceName(_cachedClientWidth, _cachedClientHeight);
        LogMessage?.Invoke($"Sunshine log signal: CLIENT CONNECTED! Requested client resolution: {_cachedClientWidth}x{_cachedClientHeight} ({device}), FPS: {_cachedClientFps}");

        SessionStarted?.Invoke(new StreamSessionStartedEventArgs
        {
            ClientWidth = _cachedClientWidth,
            ClientHeight = _cachedClientHeight,
            ClientFps = _cachedClientFps,
            DeviceDescription = device
        });
    }

    public static string GuessDeviceName(int width, int height)
    {
        if (width <= 0 || height <= 0) return "Streaming Client";

        // iPad Air M2 & standard Air/Pro
        if ((width == 2360 && height == 1640) || (width == 1640 && height == 2360))
            return "iPad Air M2 / Air 11-inch";
        if ((width == 2048 && height == 1536) || (width == 1536 && height == 2048))
            return "iPad Retina / iPad Mini";
        if ((width == 2732 && height == 2048) || (width == 2048 && height == 2732))
            return "iPad Pro 12.9-inch";
        if ((width == 2420 && height == 1668) || (width == 1668 && height == 2420))
            return "iPad Pro 11-inch";
        if ((width == 2160 && height == 1620) || (width == 1620 && height == 2160))
            return "iPad 10.2-inch";

        // Handhelds
        if ((width == 1280 && height == 800) || (width == 800 && height == 1280))
            return "Steam Deck (16:10)";
        if ((width == 1920 && height == 1080) || (width == 1080 && height == 1920))
            return "ROG Ally / Standard 1080p";
        if ((width == 2560 && height == 1600) || (width == 1600 && height == 2560))
            return "Lenovo Legion Go (16:10)";

        // Aspect ratio approximation
        double ratio = (double)width / height;
        if (Math.Abs(ratio - 4.0 / 3.0) < 0.05 || Math.Abs(ratio - 1.439) < 0.05)
            return "iPad / 4:3 Tablet";
        if (Math.Abs(ratio - 16.0 / 10.0) < 0.05)
            return "16:10 Handheld";
        if (Math.Abs(ratio - 16.0 / 9.0) < 0.05)
            return "16:9 Widescreen";

        return $"{width}x{height} Client";
    }

    public void Dispose()
    {
        Stop();
    }
}
