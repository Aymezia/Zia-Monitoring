using System.Runtime.InteropServices;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed record RefreshRateReading(string DeviceName, string FriendlyName, int CurrentHz, int MaxHz)
{
    /// <summary>Écart ≥ 20 Hz : on ignore les petits écarts VRR normaux (ex: 143 vs 144).</summary>
    public bool IsMismatch => MaxHz - CurrentHz >= 20;

    public bool IsHealthy => !IsMismatch;

    public string Label => IsMismatch
        ? $"{FriendlyName} : {CurrentHz} Hz actif, {MaxHz} Hz disponible — passez en {MaxHz} Hz dans les paramètres d'affichage."
        : $"{FriendlyName} : {CurrentHz} Hz (max {MaxHz} Hz)";
}

/// <summary>
/// Détecte les écrans dont le taux de rafraîchissement actif est nettement
/// inférieur au maximum supporté à la même résolution — cas classique d'un
/// écran 144 Hz/G-Sync/FreeSync resté configuré en 60 Hz après un
/// branchement ou une réinitialisation pilote.
/// </summary>
public static class RefreshRateDetector
{
    private const int EnumCurrentSettings = -1;
    private const uint AttachedToDesktop = 0x1;
    private const int MaxModesPerDisplay = 500;

    public static IReadOnlyList<RefreshRateReading> DetectAll()
    {
        var results = new List<RefreshRateReading>();

        try
        {
            var device = NewDisplayDevice();
            for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device = NewDisplayDevice())
            {
                if ((device.StateFlags & AttachedToDesktop) == 0)
                    continue;

                var current = NewDevMode();
                if (!EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref current))
                    continue;

                var maxHz = current.dmDisplayFrequency;
                var mode = NewDevMode();
                for (var modeNum = 0; modeNum < MaxModesPerDisplay && EnumDisplaySettings(device.DeviceName, modeNum, ref mode); modeNum++, mode = NewDevMode())
                {
                    if (mode.dmPelsWidth == current.dmPelsWidth
                        && mode.dmPelsHeight == current.dmPelsHeight
                        && mode.dmDisplayFrequency > maxHz)
                    {
                        maxHz = mode.dmDisplayFrequency;
                    }
                }

                var friendlyName = string.IsNullOrWhiteSpace(device.DeviceString) ? device.DeviceName : device.DeviceString;
                results.Add(new RefreshRateReading(device.DeviceName, friendlyName, current.dmDisplayFrequency, maxHz));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Détection du taux de rafraîchissement impossible", ex);
        }

        return results;
    }

    private static DisplayDevice NewDisplayDevice() => new() { cb = Marshal.SizeOf<DisplayDevice>() };

    private static DevMode NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DevMode>() };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

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
