using System.Runtime.InteropServices;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed record MonitorInfo(string FriendlyName, int Width, int Height, int RefreshHz, bool? HdrSupported, bool? HdrActive)
{
    public string ResolutionLabel => $"{Width}x{Height} @ {RefreshHz} Hz";

    public string HdrLabel => HdrSupported switch
    {
        true => (HdrActive ?? false) ? "HDR actif" : "HDR supporté, désactivé",
        false => "HDR non supporté",
        null => "HDR : indéterminé"
    };
}

/// <summary>
/// Résolution/fréquence via EnumDisplayDevices+EnumDisplaySettings (même
/// approche fiable que RefreshRateDetector). Le support HDR utilise l'API
/// CCD (QueryDisplayConfig + DisplayConfigGetDeviceInfo), plus récente et
/// moins universellement testée : toute erreur retombe sur "indéterminé"
/// plutôt que de faire échouer la détection de résolution/fréquence.
/// </summary>
public static class MonitorInfoCollector
{
    private const int EnumCurrentSettings = -1;
    private const uint AttachedToDesktop = 0x1;
    private const uint QdcOnlyActivePaths = 0x2;
    private const int DisplayConfigDeviceInfoGetSourceName = 1;
    private const int DisplayConfigDeviceInfoGetAdvancedColorInfo = 9;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    public static IReadOnlyList<MonitorInfo> DetectAll()
    {
        var results = new List<MonitorInfo>();

        try
        {
            var hdrByGdiName = SafeGetHdrInfoByGdiDeviceName();

            var device = NewDisplayDevice();
            for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device = NewDisplayDevice())
            {
                if ((device.StateFlags & AttachedToDesktop) == 0)
                    continue;

                var current = NewDevMode();
                if (!EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref current))
                    continue;

                var friendlyName = string.IsNullOrWhiteSpace(device.DeviceString) ? device.DeviceName : device.DeviceString;
                hdrByGdiName.TryGetValue(device.DeviceName, out var hdr);

                results.Add(new MonitorInfo(
                    friendlyName,
                    current.dmPelsWidth,
                    current.dmPelsHeight,
                    current.dmDisplayFrequency,
                    hdr.Supported,
                    hdr.Active));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Détection des moniteurs connectés impossible", ex);
        }

        return results;
    }

    private static Dictionary<string, (bool? Supported, bool? Active)> SafeGetHdrInfoByGdiDeviceName()
    {
        var result = new Dictionary<string, (bool?, bool?)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var status = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
            if (status != ErrorSuccess || pathCount == 0)
                return result;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new byte[modeCount * ModeInfoStride];

            var handle = GCHandle.Alloc(modes, GCHandleType.Pinned);
            try
            {
                status = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, handle.AddrOfPinnedObject(), IntPtr.Zero);
            }
            finally
            {
                handle.Free();
            }

            if (status != ErrorSuccess)
                return result;

            foreach (var path in paths.Take((int)pathCount))
            {
                try
                {
                    var gdiName = GetSourceGdiDeviceName(path.SourceInfo.AdapterId, path.SourceInfo.Id);
                    if (gdiName is null)
                        continue;

                    var (supported, active) = GetAdvancedColorInfo(path.TargetInfo.AdapterId, path.TargetInfo.Id);
                    result[gdiName] = (supported, active);
                }
                catch
                {
                    // Une cible individuelle en échec ne doit pas invalider les autres.
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Lecture du support HDR (CCD) impossible, statut marqué indéterminé", ex);
        }

        return result;
    }

    private static string? GetSourceGdiDeviceName(LUID adapterId, uint sourceId)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            Header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                Type = DisplayConfigDeviceInfoGetSourceName,
                Size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                AdapterId = adapterId,
                Id = sourceId
            }
        };

        var status = DisplayConfigGetDeviceInfo(ref request);
        return status == ErrorSuccess ? request.ViewGdiDeviceName : null;
    }

    private static (bool? Supported, bool? Active) GetAdvancedColorInfo(LUID adapterId, uint targetId)
    {
        var request = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            Header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                Type = DisplayConfigDeviceInfoGetAdvancedColorInfo,
                Size = Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                AdapterId = adapterId,
                Id = targetId
            }
        };

        var status = DisplayConfigGetDeviceInfo(ref request);
        if (status != ErrorSuccess)
            return (null, null);

        var supported = (request.Value & 0x1) != 0;
        var enabled = (request.Value & 0x2) != 0;
        return (supported, enabled);
    }

    private static DisplayDevice NewDisplayDevice() => new() { cb = Marshal.SizeOf<DisplayDevice>() };
    private static DevMode NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DevMode>() };

    // Taille fixe d'un DISPLAYCONFIG_MODE_INFO (union source/target/desktop image, 64 octets en x64) :
    // le contenu n'est jamais lu ici, seul le pas entre éléments doit être correct pour QueryDisplayConfig.
    private const int ModeInfoStride = 64;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements, IntPtr modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO request);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DISPLAYCONFIG_RATIONAL RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO SourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int Type;
        public int Size;
        public LUID AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
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
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
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
}
