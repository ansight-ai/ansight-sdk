namespace Ansight.Pairing.Models;

public sealed class DeviceProfile
{
    public string? Manufacturer { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Product { get; set; }
    public int? DeviceClassCode { get; set; }
    public bool? IsEmulator { get; set; }
    public string? Locale { get; set; }
    public string? TimeZone { get; set; }
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public string? OsBuild { get; set; }
    public int? ApiLevel { get; set; }
    public string? CpuArch { get; set; }
    public int? CpuCoreCount { get; set; }
    public List<string>? AbiList { get; set; }
    public string? ChipModel { get; set; }
    public long? MemoryTotalMb { get; set; }
    public long? MemoryFreeMb { get; set; }
    public long? StorageTotalMb { get; set; }
    public long? StorageFreeMb { get; set; }
    public DeviceBatteryProfile? Battery { get; set; }
    public DeviceDisplayProfile? Display { get; set; }
    public DeviceGpuProfile? Gpu { get; set; }
    public DeviceNetworkProfile? Network { get; set; }
    public DeviceThermalProfile? Thermal { get; set; }
}
