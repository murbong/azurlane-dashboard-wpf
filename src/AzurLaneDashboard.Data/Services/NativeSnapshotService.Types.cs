using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private sealed record ShipStatBlock(
        int Durability,
        int Cannon,
        int Torpedo,
        int AntiAircraft,
        int Air,
        int Reload,
        int ReloadGrowth,
        int ReloadGrowthExtra,
        int Hit,
        int Dodge,
        int Speed,
        int Luck,
        int AntiSub,
        int OilConsumption,
        int ArmorType,
        int StrengthenId,
        int ReloadStrengthenLimit,
        int ReloadStrengthenExpRatio,
        BlueprintStats? BlueprintStats,
        IReadOnlyDictionary<int, IReadOnlyList<int>> TransformReloadEffects,
        IReadOnlyList<int> DefaultEquipmentIds,
        IReadOnlyList<int> DefaultEquipmentTypes,
        IReadOnlyList<int> AirSlotWeights,
        IReadOnlyList<int> BaseAttributes,
        IReadOnlyList<int> AttributeGrowth,
        IReadOnlyList<int> AttributeGrowthExtra,
        ShipStrengthenStats? StrengthenStats);

    private sealed record ShipEffectiveStats(
        int Durability,
        int Cannon,
        int Torpedo,
        int AntiAircraft,
        int Air,
        int Reload,
        int Hit,
        int Dodge,
        int Speed,
        int Luck,
        int AntiSub,
        int OilConsumption,
        int ArmorType);

    private sealed record ShipStrengthenStats(
        IReadOnlyList<int> Limits,
        IReadOnlyList<int> ExpRatios);

    private sealed record BlueprintStats(
        int MaxLevel,
        IReadOnlyList<int> AttrExp,
        IReadOnlyList<BlueprintStatLevel> Levels);

    private sealed record BlueprintStatLevel(int Level, int NeedExp, IReadOnlyList<int> Effects);

    private sealed record BlueprintDefinition(IReadOnlyList<int> AttrExp, IReadOnlyList<int> StrengthenEffectIds);

    private sealed record ShipStrengthState(
        IReadOnlyDictionary<int, int> AttributeExp,
        int BlueprintLevel,
        int BlueprintExp)
    {
        public int ReloadStrengthExp => AttributeExp.GetValueOrDefault(ReloadStrengthAttrId);

        public int StrengthExp(int attributeId) => AttributeExp.GetValueOrDefault(attributeId);

        public static ShipStrengthState Empty { get; } = new(new Dictionary<int, int>(), 0, 0);
    }

    private sealed record BagEquipment(int TemplateId, int Count, int EnhanceLevel);

    private sealed record PacketRecord(
        int RowId,
        string Source,
        string Direction,
        int PacketId,
        string PacketName,
        int Index,
        JsonElement Json);

    private sealed record SnapshotMaps(
        JsonElement Ships,
        JsonElement Skins,
        JsonElement Equipment,
        JsonElement FireRateEquipment,
        JsonElement EquipmentSkins,
        JsonElement SpWeapons,
        JsonElement Resources,
        JsonElement Skills,
        JsonElement ShipLevels,
        JsonElement Stages,
        JsonElement FleetTech,
        JsonElement FleetTechNation,
        JsonElement ShipBlueprints,
        JsonElement ShipTransforms,
        JsonElement RegalShips,
        JsonElement RegalEquips,
        JsonElement RegalGearMap,
        JsonElement RegalAugments,
        JsonElement RegalChangelog,
        JsonElement MetaBosses,
        JsonElement MetaBossGuides,
        JsonElement Commanders,
        JsonElement MeowfficerGuide,
        JsonElement WorldTargets,
        JsonElement PersonalTasks,
        JsonElement MonthShop,
        JsonElement OilCosts,
        JsonElement Commissions,
        JsonElement AuctionCollection,
        JsonElement AppSettings)
    {
        public int RegalShipCount => RegalShips.ValueKind == JsonValueKind.Array ? RegalShips.GetArrayLength() : 0;
    }

    private sealed record FullEquipmentChange(string ShipUid, int Position, int EquipmentId);

    private sealed record FullEquipmentSkinChange(string ShipUid, int Position, int SkinId);

    private sealed record CaptureStatus(bool Running, string Label, string Detail);

    private sealed record CurrentStateInfo(
        string Mode,
        int LastDecodedRowId,
        string LastStatus,
        string LastArea,
        string LastDetail,
        string LastError);

    private sealed record FleetAssignment(
        string ShipUid,
        int FleetId,
        string FleetName,
        int SlotIndex,
        string CommanderSummary)
    {
        public string DisplayName
        {
            get
            {
                if (FleetId <= 0)
                {
                    return "미편성";
                }

                var defaultName = ShortName;
                var name = FleetName.Trim();
                return string.IsNullOrWhiteSpace(name) || string.Equals(name, defaultName, StringComparison.Ordinal)
                    ? defaultName
                    : $"{defaultName} · {name}";
            }
        }

        public string ShortName => FleetId switch
        {
            >= 1 and <= 6 => $"{FleetId.ToString(CultureInfo.InvariantCulture)}함대",
            >= 11 and <= 12 => $"잠수함대 {(FleetId - 10).ToString(CultureInfo.InvariantCulture)}",
            _ => $"{FleetId.ToString(CultureInfo.InvariantCulture)}편성"
        };
    }

    private sealed record FleetGroupLayout(
        int FleetId,
        string FleetName,
        string CommanderSummary,
        IReadOnlyList<FleetAssignment> Ships)
    {
        public string DisplayName => new FleetAssignment("", FleetId, FleetName, 0, CommanderSummary).DisplayName;
    }

    private sealed record FleetLayout(
        IReadOnlyDictionary<string, FleetAssignment> Assignments,
        IReadOnlyList<FleetGroupLayout> Groups,
        bool HasFleetData)
    {
        public static FleetLayout Empty { get; } =
            new(new Dictionary<string, FleetAssignment>(StringComparer.Ordinal), [], false);
    }

    private sealed record StageInfo(string Label, string Name, string Detail, int Type);
}
