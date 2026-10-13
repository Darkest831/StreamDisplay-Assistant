using System;
using System.Runtime.InteropServices;

namespace StreamDisplayAssistant.Native;

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int NotImpl1();
    [PreserveSig]
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
}

[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    int NotImpl1();
    int NotImpl2();
    int GetChannelCount(out uint pnChannelCount);
    int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
    int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
    int GetMasterVolumeLevel(out float pfLevelDB);
    int GetMasterVolumeLevelScalar(out float pfLevel);
    int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
    int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
    int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
    int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

/// <summary>
/// 100% Native Windows Core Audio controller using built-in Windows COM APIs.
/// Zero third-party dependencies, zero open-source libraries.
/// Automatically mutes host PC speakers while streaming so sound plays exclusively on the remote client.
/// </summary>
public static class AudioControlService
{
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static bool _mutedByApp = false;
    private static bool _originalMuteState = false;

    private static IAudioEndpointVolume? GetMasterAudioEndpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            // eRender = 0 (Playback), eMultimedia = 1 (Default audio role)
            int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
            if (hr != 0 || device == null) return null;

            var iid = IID_IAudioEndpointVolume;
            hr = device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var endpointObj);
            if (hr != 0 || endpointObj == null) return null;

            return (IAudioEndpointVolume)endpointObj;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if the master audio output is currently muted.
    /// </summary>
    public static bool IsMuted()
    {
        try
        {
            var endpoint = GetMasterAudioEndpoint();
            if (endpoint == null) return false;

            int hr = endpoint.GetMute(out bool isMuted);
            return hr == 0 && isMuted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Mutes the host PC speakers when streaming starts.
    /// Remembers the previous mute state so it can be accurately restored.
    /// </summary>
    public static bool MuteHostAudio()
    {
        try
        {
            var endpoint = GetMasterAudioEndpoint();
            if (endpoint == null) return false;

            endpoint.GetMute(out _originalMuteState);

            if (!_originalMuteState)
            {
                var guid = Guid.Empty;
                int hr = endpoint.SetMute(true, ref guid);
                if (hr == 0)
                {
                    _mutedByApp = true;
                    return true;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Restores the host PC audio to its pre-streaming state.
    /// Only un-mutes if it was muted by StreamDisplay Assistant.
    /// </summary>
    public static bool RestoreHostAudio()
    {
        try
        {
            if (!_mutedByApp) return true;

            var endpoint = GetMasterAudioEndpoint();
            if (endpoint == null) return false;

            var guid = Guid.Empty;
            int hr = endpoint.SetMute(_originalMuteState, ref guid);
            _mutedByApp = false;
            return hr == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Toggles the master mute state (for global hotkey).
    /// </summary>
    public static bool ToggleMute()
    {
        try
        {
            var endpoint = GetMasterAudioEndpoint();
            if (endpoint == null) return false;

            endpoint.GetMute(out bool isMuted);
            var guid = Guid.Empty;
            int hr = endpoint.SetMute(!isMuted, ref guid);
            return hr == 0;
        }
        catch
        {
            return false;
        }
    }
}
