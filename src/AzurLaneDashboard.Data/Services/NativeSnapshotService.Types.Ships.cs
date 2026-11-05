using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private sealed class FullShipState
    {
        public int SourceRowId { get; init; }
        public int TemplateId { get; init; }
        public int Level { get; set; }
        public int Exp { get; init; }
        public int Energy { get; init; }
        public int Intimacy { get; init; }
        public int ReloadStrengthExp { get; init; }
        public ShipStrengthState Strength { get; init; } = ShipStrengthState.Empty;
        public int CreateTime { get; init; }
        public int SkinId { get; init; }
        public string CustomName { get; init; } = "";
        public bool Locked { get; init; }
        public bool HasActiveState { get; init; }
        public List<SkillState> Skills { get; init; } = new();
        public List<EquippedItemState> Equipment { get; init; } = new();
        public List<int> TransformIds { get; init; } = new();
        public IReadOnlyDictionary<int, int> TransformLevels { get; init; } = new Dictionary<int, int>();

        public static FullShipState FromJson(JsonElement ship, int rowId)
        {
            var equipment = Array(ship, "equip_info_list")
                .Select(item => new EquippedItemState(JsonInt(item, "id"), JsonInt(item, "skin_id")))
                .ToList();
            while (equipment.Count < 5)
            {
                equipment.Add(new EquippedItemState(0, 0));
            }

            var strength = ReadShipStrengthState(ship);
            var transformLevels = ReadTransformLevels(ship);
            return new FullShipState
            {
                SourceRowId = rowId,
                TemplateId = JsonInt(ship, "template_id"),
                Level = JsonInt(ship, "level"),
                Exp = JsonInt(ship, "exp"),
                Energy = JsonInt(ship, "energy"),
                Intimacy = JsonInt(ship, "intimacy"),
                ReloadStrengthExp = strength.ReloadStrengthExp,
                Strength = strength,
                CreateTime = JsonInt(ship, "create_time"),
                SkinId = JsonInt(ship, "skin_id"),
                CustomName = FirstNonEmpty(JsonString(ship, "custom_name"), JsonString(ship, "name")),
                Locked = JsonInt(ship, "is_locked") != 0,
                HasActiveState = Child(ship, "state").ValueKind == JsonValueKind.Object &&
                                 Child(ship, "state").EnumerateObject().Any(),
                Skills = Array(ship, "skill_id_list").Select(skill => new SkillState(JsonInt(skill, "skill_id"),
                    JsonInt(skill, "skill_lv"), JsonInt(skill, "skill_exp"))).ToList(),
                Equipment = equipment,
                TransformIds = transformLevels.Keys.ToList(),
                TransformLevels = transformLevels
            };
        }

        public void SetEquipment(int position, int templateId, int skinId)
        {
            if (position <= 0)
            {
                return;
            }

            while (Equipment.Count < position)
            {
                Equipment.Add(new EquippedItemState(0, 0));
            }

            Equipment[position - 1] = new EquippedItemState(templateId, skinId);
        }

        public void SetEquipmentSkin(int position, int skinId)
        {
            if (position <= 0)
            {
                return;
            }

            while (Equipment.Count < position)
            {
                Equipment.Add(new EquippedItemState(0, 0));
            }

            var current = Equipment[position - 1];
            Equipment[position - 1] = new EquippedItemState(current.TemplateId, skinId);
        }
    }

    private sealed record SkillState(int Id, int Level, int Exp);

    private sealed record EquippedItemState(int TemplateId, int SkinId);

    private sealed class EquipmentItemState
    {
        public int Slot { get; init; }
        public bool Empty { get; init; }
        public int TemplateId { get; init; }
        public string Name { get; init; } = "";
        public string IconUrl { get; init; } = "";
        public string SkinIconUrl { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string RarityClass { get; init; } = "";
        public int EnhanceLevel { get; init; }
        public string TypeGroup { get; init; } = "";
        public IReadOnlyList<EquipmentAttributeRow> Attributes { get; init; } = [];
        public int ReloadBonus { get; init; }
        public int SkinId { get; init; }
        public string SkinName { get; init; } = "";
    }

    private sealed class ShipState
    {
        public string Uid { get; init; } = "";
        public int TemplateId { get; init; }
        public int GroupId { get; init; }
        public string Name { get; init; } = "";
        public string CustomName { get; init; } = "";
        public string DisplayName => string.IsNullOrWhiteSpace(CustomName) ? Name : $"{Name} ({CustomName})";
        public string IconUrl { get; init; } = "";
        public string BackgroundUrl { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string RarityClass { get; init; } = "";
        public int RarityScore { get; init; }
        public int Level { get; set; }
        public int LimitBreak { get; init; }
        public int EnhanceLevel { get; init; }
        public string EnhanceText { get; init; } = "-";
        public int Exp { get; set; }
        public int ExpCurrent { get; set; }
        public int ExpRequired { get; set; }
        public double ExpPercent { get; set; }
        public bool ExpIsMax { get; set; }
        public long TotalExp { get; init; }
        public int Energy { get; init; }
        public int Intimacy { get; init; }
        public int CreateTime { get; init; }
        public string StatsSummary { get; init; } = "";
        public string StatsDetail { get; set; } = "";
        public IReadOnlyList<ShipStatRow> StatRows { get; set; } = [];
        public int Reload { get; set; }
        public int ShipTypeId { get; init; }
        public int NationId { get; init; }
        public IReadOnlyList<int> DefaultEquipmentIds { get; init; } = [];
        public IReadOnlyList<int> DefaultEquipmentTypes { get; init; } = [];
        public IReadOnlyList<int> AirSlotWeights { get; init; } = [];
        public int SkinId { get; init; }
        public string SkinName { get; init; } = "";
        public bool Locked { get; init; }
        public bool IsProposed { get; init; }
        public List<SkillState> Skills { get; init; } = new();
        public string SkillSummary { get; init; } = "";
        public int SkillGap { get; init; }
        public List<EquipmentItemState> Equipment { get; init; } = new();
        public string EquippedSummary { get; init; } = "";
        public int EmptySlots { get; init; }
        public int FleetId { get; set; }
        public string FleetLabel { get; set; } = "";
        public SpecialEquipmentRow? SpecialEquipment { get; init; }
        public string SpecialEquipmentSummary { get; init; } = "";
        public string FleetTechGetBonus { get; init; } = "";
        public string FleetTechLevelBonus { get; init; } = "";
        public bool FleetTechLevelPending { get; set; }
        public bool FleetTechLevelReloadPending { get; set; }
        public string CommunityLabel { get; set; } = "";
        public string CommunitySummary { get; set; } = "";
        public int CommunityScore { get; set; }
        public int CommunityBestScore { get; set; }
        public List<ShipCommunityRatingRow> CommunityRatings { get; set; } = new();
        public List<ShipRecommendationGroupRow> GearRecommendationGroups { get; set; } = new();
        public List<ShipRecommendationRow> AugmentRecommendations { get; set; } = new();
        public int GearRecommendationOwned { get; set; }
        public int GearRecommendationTotal { get; set; }
        public int AugmentRecommendationOwned { get; set; }
        public int AugmentRecommendationTotal { get; set; }
        public int? RunsTo120Avg { get; set; }
        public int? RunsTo125Avg { get; set; }
        public int RecentBattleExpAvg { get; set; }
        public int SnapshotRowId { get; init; }
        public bool HasFullState { get; init; }
        public bool IsBulin { get; init; }
    }

    private sealed class EquipmentCounter
    {
        public int TemplateId { get; init; }
        public string Name { get; init; } = "";
        public string IconUrl { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string RarityClass { get; init; } = "";
        public string EquipmentTypeGroup { get; init; } = "";
        public IReadOnlyList<EquipmentAttributeRow> Attributes { get; set; } = [];
        public int EnhanceLevel { get; set; }
        public int Count { get; set; }
        public int BagCount { get; set; }
        public int EquippedCount { get; set; }
        public List<string> EquippedBy { get; } = new();
        public List<EquipmentEquippedShipRow> EquippedShips { get; } = new();
    }

}
