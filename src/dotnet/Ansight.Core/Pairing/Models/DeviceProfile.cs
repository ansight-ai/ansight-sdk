namespace Ansight.Pairing.Models;

/// <summary>
/// Device-level metadata included in a <see cref="DeviceAppProfile"/>.
/// </summary>
public sealed class DeviceProfile
{
    /// <summary>
    /// Device manufacturer name.
    /// </summary>
    public string? Manufacturer { get; set; }

    /// <summary>
    /// Device brand name.
    /// </summary>
    public string? Brand { get; set; }

    /// <summary>
    /// Device model name.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Device product name or sku, when available.
    /// </summary>
    public string? Product { get; set; }

    /// <summary>
    /// Protocol-defined device class code.
    /// </summary>
    public int? DeviceClassCode { get; set; }

    /// <summary>
    /// Indicates whether the app appears to be running on an emulator or simulator.
    /// </summary>
    public bool? IsEmulator { get; set; }

    /// <summary>
    /// Current UI locale for the app or device.
    /// </summary>
    public string? Locale { get; set; }

    /// <summary>
    /// Current local time-zone identifier.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Operating-system family name.
    /// </summary>
    public string? OsName { get; set; }

    /// <summary>
    /// Operating-system version string.
    /// </summary>
    public string? OsVersion { get; set; }

    /// <summary>
    /// Operating-system build string.
    /// </summary>
    public string? OsBuild { get; set; }

    /// <summary>
    /// Platform API level, when the operating system exposes one.
    /// </summary>
    public int? ApiLevel { get; set; }

    /// <summary>
    /// Process CPU architecture.
    /// </summary>
    public string? CpuArch { get; set; }

    /// <summary>
    /// Number of logical CPU cores available to the process.
    /// </summary>
    public int? CpuCoreCount { get; set; }

    /// <summary>
    /// Supported application binary interfaces or architectures.
    /// </summary>
    public List<string>? AbiList { get; set; }

    /// <summary>
    /// SoC or chip model string, when available.
    /// </summary>
    public string? ChipModel { get; set; }

    /// <summary>
    /// Total physical memory in megabytes, when available.
    /// </summary>
    public long? MemoryTotalMb { get; set; }

    /// <summary>
    /// Free physical memory in megabytes, when available.
    /// </summary>
    public long? MemoryFreeMb { get; set; }

    /// <summary>
    /// Total storage capacity in megabytes, when available.
    /// </summary>
    public long? StorageTotalMb { get; set; }

    /// <summary>
    /// Free storage capacity in megabytes, when available.
    /// </summary>
    public long? StorageFreeMb { get; set; }

    /// <summary>
    /// Battery metadata for the device.
    /// </summary>
    public DeviceBatteryProfile? Battery { get; set; }

    /// <summary>
    /// Display metadata for the device.
    /// </summary>
    public DeviceDisplayProfile? Display { get; set; }

    /// <summary>
    /// GPU metadata for the device.
    /// </summary>
    public DeviceGpuProfile? Gpu { get; set; }

    /// <summary>
    /// Network metadata for the device.
    /// </summary>
    public DeviceNetworkProfile? Network { get; set; }

    /// <summary>
    /// Thermal metadata for the device.
    /// </summary>
    public DeviceThermalProfile? Thermal { get; set; }
}
