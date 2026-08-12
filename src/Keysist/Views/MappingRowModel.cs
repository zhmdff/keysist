using Keysist.Config;
using Keysist.Services;
using Microsoft.UI.Xaml;

namespace Keysist.Views;

public sealed class MappingRowModel
{
    public required DeviceConfig Device { get; init; }

    public KeyMapping? Mapping { get; init; }

    public bool ShowDeviceLabel { get; init; }

    public Visibility ShowDeviceLabelVisibility =>
        ShowDeviceLabel ? Visibility.Visible : Visibility.Collapsed;

    public string DeviceName =>
        string.IsNullOrWhiteSpace(Device.Name) ? Device.DevicePathMatch : Device.Name;

    public string TriggerSummary => Mapping is not null
        ? MappingSummaryHelper.FormatTriggerSummary(Mapping)
        : "[any key]";

    public string ActionSummary => MappingSummaryHelper.FormatActionSummary(
        Mapping?.Action ?? Device.AnyKeyAction ?? new ActionConfig());

    public bool Swallow => Mapping?.Swallow ?? Device.DefaultSwallow;
}

public sealed class DeviceSectionModel
{
    public required DeviceConfig Device { get; init; }

    public required IReadOnlyList<MappingRowModel> Mappings { get; init; }

    public string Name =>
        string.IsNullOrWhiteSpace(Device.Name) ? Device.DevicePathMatch : Device.Name;

    public string PathHint => Device.DevicePathMatch;

    public string MappingCountLabel =>
        Mappings.Count == 0 ? "No mappings" : $"{Mappings.Count} mapping{(Mappings.Count == 1 ? "" : "s")}";
}
