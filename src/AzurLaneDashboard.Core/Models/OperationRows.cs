namespace AzurLaneDashboard.Core.Models;

public sealed class OperationLogLine
{
    public DateTimeOffset Time { get; init; } = DateTimeOffset.Now;
    public string Text { get; init; } = "";
    public string DisplayText => $"[{Time:HH:mm:ss}] {Text}";
}

public sealed class CaptureStatusViewRow
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Kind { get; init; } = "";
}

public sealed class UpdateStatusViewRow
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Kind { get; init; } = "";
}

public sealed class ShipFilterState
{
    public string SortOption { get; init; } = "Lv";
    public bool SortAscending { get; init; }
    public string Rarity { get; init; } = "전체";
    public string ShipType { get; init; } = "전체";
    public string Nation { get; init; } = "전체";
    public string Extra { get; init; } = "제한 없음";
}

public sealed class EquipmentFilterState
{
    public string SortOption { get; init; } = "레어도";
    public bool SortAscending { get; init; }
    public string Rarity { get; init; } = "전체";
    public string EquipmentType { get; init; } = "전체";
    public string Extra { get; init; } = "제한 없음";
}
