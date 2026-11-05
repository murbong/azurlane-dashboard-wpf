using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.Services;

public sealed class DashboardExportData
{
    public string DatabasePath { get; init; } = "";
    public string LastLoadedAt { get; init; } = "";
    public string SnapshotSource { get; init; } = "";
    public string SnapshotWarning { get; init; } = "";
    public int DockUsed { get; init; }
    public int DockMax { get; init; }
    public int DockExemptCount { get; init; }
    public int EquipmentBagCount { get; init; }
    public int EquipmentBagMax { get; init; }
    public int EquipmentEquippedCount { get; init; }
    public IReadOnlyList<ShipRow> Ships { get; init; } = [];
    public IReadOnlyList<EquipmentRow> Equipment { get; init; } = [];
    public IReadOnlyList<EquipmentRow> EquipmentRecommendations { get; init; } = [];
    public IReadOnlyList<SpecialEquipmentRow> SpecialEquipment { get; init; } = [];
    public IReadOnlyList<EquipmentSkinRow> EquipmentSkins { get; init; } = [];
    public IReadOnlyList<LevelPriorityRow> LevelPriorities { get; init; } = [];
    public IReadOnlyList<GearGapRow> GearGaps { get; init; } = [];
    public IReadOnlyList<SkillPriorityRow> SkillPriorities { get; init; } = [];
    public IReadOnlyList<FleetDamageRow> FleetDamageRows { get; init; } = [];
    public IReadOnlyList<ResourceRow> Resources { get; init; } = [];
    public IReadOnlyList<AcademyRoomRow> AcademyRooms { get; init; } = [];
    public IReadOnlyList<ResearchQueueRow> ResearchQueue { get; init; } = [];
    public IReadOnlyList<CommissionRow> Commissions { get; init; } = [];
    public IReadOnlyList<BuildQueueRow> Builds { get; init; } = [];
}

public static class DashboardWorkbookExporter
{
    private const string ContentTypeNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Export(string path, DashboardExportData data)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("내보낼 파일 경로가 비어 있습니다.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sheets = BuildSheets(data);
        using var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);

        WriteContentTypes(archive, sheets.Count);
        WriteRootRelationships(archive);
        WriteWorkbook(archive, sheets);
        WriteWorkbookRelationships(archive, sheets.Count);
        WriteStyles(archive);

        for (var index = 0; index < sheets.Count; index++)
        {
            WriteWorksheet(archive, sheets[index], index + 1);
        }
    }

    private static IReadOnlyList<WorkbookSheet> BuildSheets(DashboardExportData data)
    {
        return
        [
            BuildSummarySheet(data),
            BuildSheet("도크", data.Ships, ShipColumns()),
            BuildSheet("장비", data.Equipment, EquipmentColumns()),
            BuildSheet("특수장비", data.SpecialEquipment, SpecialEquipmentColumns()),
            BuildSheet("스킬", data.SkillPriorities, SkillPriorityColumns())
        ];
    }

    private static WorkbookSheet BuildSummarySheet(DashboardExportData data)
    {
        object?[][] rows =
        [
            ["생성일", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)],
            ["마지막 로드", data.LastLoadedAt],
            ["DB", data.DatabasePath],
            ["스냅샷", data.SnapshotSource],
            ["경고", data.SnapshotWarning],
            ["도크", $"{data.DockUsed:N0}/{data.DockMax:N0}"],
            ["도크 면제", data.DockExemptCount],
            ["장비 창고", $"{data.EquipmentBagCount:N0}/{data.EquipmentBagMax:N0}"],
            ["장착 장비", data.EquipmentEquippedCount],
            ["함선 수", data.Ships.Count],
            ["장비 종류 수", data.Equipment.Count],
            ["특수장비 수", data.SpecialEquipment.Count],
            ["장비스킨 수", data.EquipmentSkins.Count]
        ];

        return new WorkbookSheet(
            "요약",
            [new WorkbookColumn("항목", 18), new WorkbookColumn("값", 48)],
            rows);
    }

    private static IReadOnlyList<ExportColumn<ShipRow>> ShipColumns() =>
    [
        new("UID", ship => ship.ShipUid, 13),
        new("템플릿ID", ship => ship.TemplateId, 12),
        new("이름", ship => ship.Name, 22),
        new("애칭", ship => ship.CustomName, 18),
        new("표시명", ship => ship.DisplayName, 28),
        new("등급", ship => Rarity(ship.RarityTier, ship.Rarity), 10),
        new("레벨", ship => ship.Level, 8),
        new("함종", ship => ship.ShipTypeLabel, 12),
        new("진영", ship => ship.NationLabel, 12),
        new("한계돌파", ship => ship.LimitBreak, 10),
        new("강화", ship => ship.EnhanceText, 12),
        new("호감도", ship => ship.Intimacy, 10),
        new("기분", ship => PositiveOrBlank(ship.MoodValue), 10),
        new("스킨", ship => ship.SkinName, 22),
        new("잠금", ship => YesNo(ship.Locked), 8),
        new("서약", ship => YesNo(ship.IsProposed), 8),
        new("내구", ship => StatNumber(ship, "내구"), 10),
        new("포격", ship => StatNumber(ship, "포격"), 10),
        new("뇌장", ship => StatNumber(ship, "뇌장"), 10),
        new("대공", ship => StatNumber(ship, "대공"), 10),
        new("대잠", ship => StatNumber(ship, "대잠"), 10),
        new("항공", ship => StatNumber(ship, "항공"), 10),
        new("장전", ship => StatNumber(ship, "장전"), 10),
        new("기동", ship => StatNumber(ship, "기동"), 10),
        new("명중", ship => StatNumber(ship, "명중"), 10),
        new("항속", ship => StatNumber(ship, "항속"), 10),
        new("행운", ship => StatNumber(ship, "행운"), 10),
        new("장갑", ship => StatValue(ship, "장갑"), 12),
        new("소모", ship => StatNumber(ship, "소모"), 10),
        new("스탯요약", ship => ship.StatsSummary, 30),
        new("장비", ship => ship.EquippedSummary, 48),
        new("특수장비", ship => ship.SpecialEquipmentSummary, 28),
        new("스킬", ship => ship.SkillSummary, 42),
        new("빈장비칸", ship => ship.EmptySlots, 10),
        new("기술획득", ship => ship.FleetTechGetBonus, 24),
        new("기술레벨", ship => ship.FleetTechLevelBonus, 24),
        new("120기술대기", ship => YesNo(ship.FleetTechLevelPending), 12),
        new("120장전대기", ship => YesNo(ship.FleetTechLevelReloadPending), 12),
        new("커뮤니티", ship => ship.CommunityLabel, 14),
        new("커뮤니티상세", ship => ship.CommunitySummary, 32),
        new("최근EXP평균", ship => ship.RecentBattleExpAvg, 12),
        new("120까지", ship => ship.RunsTo120Avg, 12),
        new("125까지", ship => ship.RunsTo125Avg, 12),
        new("입수시간", ship => ship.CreateTimeText, 18),
        new("전체상태", ship => YesNo(ship.HasFullState), 10)
    ];

    private static IReadOnlyList<ExportColumn<EquipmentRow>> EquipmentColumns() =>
    [
        new("ID", equipment => equipment.TemplateId, 12),
        new("이름", equipment => equipment.Name, 34),
        new("등급", equipment => Rarity(equipment.RarityTier, equipment.Rarity), 10),
        new("종류", equipment => equipment.EquipmentTypeLabel, 12),
        new("강화", equipment => equipment.EnhanceLevel, 8),
        new("총수량", equipment => equipment.Count, 10),
        new("창고", equipment => equipment.BagCount, 10),
        new("장착", equipment => equipment.EquippedCount, 10),
        new("점수", equipment => equipment.Score, 10),
        new("판정", equipment => equipment.Action, 18),
        new("스탯", equipment => equipment.AttributeSummary, 32),
        new("장착함선", equipment => equipment.EquippedBySummary, 56)
    ];

    private static IReadOnlyList<ExportColumn<SpecialEquipmentRow>> SpecialEquipmentColumns() =>
    [
        new("ID", equipment => equipment.Id, 12),
        new("템플릿ID", equipment => equipment.TemplateId, 12),
        new("이름", equipment => equipment.Name, 34),
        new("등급", equipment => Rarity(equipment.RarityTier, equipment.Rarity), 10),
        new("강화", equipment => equipment.EnhanceLevel, 8),
        new("효과", equipment => equipment.Effect, 32),
        new("PT", equipment => equipment.Pt, 8),
        new("장착수", equipment => equipment.EquippedCount, 10),
        new("장착함선", equipment => equipment.EquippedBySummary, 48)
    ];

    private static IReadOnlyList<ExportColumn<EquipmentSkinRow>> EquipmentSkinColumns() =>
    [
        new("ID", skin => skin.Id, 12),
        new("이름", skin => skin.Name, 34),
        new("등급", skin => Rarity(skin.RarityTier, skin.Rarity), 10),
        new("수량", skin => skin.Count, 10),
        new("테마", skin => skin.ThemeId, 16)
    ];

    private static IReadOnlyList<ExportColumn<LevelPriorityRow>> LevelPriorityColumns() =>
    [
        new("점수", row => row.Score, 10),
        new("UID", row => row.ShipUid, 13),
        new("이름", row => row.DisplayName, 28),
        new("등급", row => Rarity(row.RarityTier, row.Rarity), 10),
        new("서약", row => YesNo(row.IsProposed), 8),
        new("레벨", row => row.Level, 8),
        new("함종", row => row.ShipTypeLabel, 12),
        new("진영", row => row.NationLabel, 12),
        new("120까지Lv", row => row.LevelsTo120, 12),
        new("125까지Lv", row => row.LevelsTo125, 12),
        new("EXP", row => row.ExpProgressText, 18),
        new("120예상", row => row.RunsTo120Text, 16),
        new("125예상", row => row.RunsTo125Text, 16),
        new("기분", row => PositiveOrBlank(row.MoodValue), 10),
        new("스킬", row => row.SkillSummary, 32),
        new("기술레벨", row => row.FleetTechLevelBonus, 24),
        new("120기술대기", row => YesNo(row.FleetTechLevelPending), 12),
        new("120장전대기", row => YesNo(row.FleetTechLevelReloadPending), 12),
        new("커뮤니티", row => row.CommunityLabel, 14),
        new("커뮤니티상세", row => row.CommunitySummary, 32),
        new("최근EXP평균", row => row.RecentBattleExpAvg, 12),
        new("입수시간", row => row.CreateTimeText, 18)
    ];

    private static IReadOnlyList<ExportColumn<GearGapRow>> GearGapColumns() =>
    [
        new("UID", row => row.ShipUid, 13),
        new("이름", row => row.DisplayName, 28),
        new("등급", row => Rarity(row.RarityTier, row.Rarity), 10),
        new("서약", row => YesNo(row.IsProposed), 8),
        new("레벨", row => row.Level, 8),
        new("함종", row => row.ShipTypeLabel, 12),
        new("진영", row => row.NationLabel, 12),
        new("빈칸", row => row.EmptySlots, 10),
        new("장비", row => row.EquippedSummary, 48),
        new("특수장비", row => row.SpecialEquipmentSummary, 28)
    ];

    private static IReadOnlyList<ExportColumn<SkillPriorityRow>> SkillPriorityColumns() =>
    [
        new("점수", row => row.Score, 10),
        new("UID", row => row.ShipUid, 13),
        new("함선", row => row.ShipName, 28),
        new("등급", row => row.Rarity, 10),
        new("서약", row => YesNo(row.IsProposed), 8),
        new("함선Lv", row => row.ShipLevel, 10),
        new("스킬ID", row => row.SkillId, 12),
        new("스킬", row => row.SkillName, 34),
        new("스킬Lv", row => row.SkillLevel, 10),
        new("최대Lv", row => row.SkillMaxLevel, 10),
        new("스킬EXP", row => row.SkillExp, 12),
        new("사유", row => row.Reason, 42)
    ];

    private static IReadOnlyList<ExportColumn<FleetDamageRow>> FleetDamageColumns() =>
    [
        new("함대ID", row => row.FleetId, 10),
        new("함대", row => row.FleetName, 18),
        new("함대슬롯", row => row.FleetSlot, 10),
        new("편성", row => row.FormationGroup, 12),
        new("지휘냥", row => row.CommanderSummary, 28),
        new("UID", row => row.ShipUid, 13),
        new("함선", row => row.ShipName, 28),
        new("함선Lv", row => row.ShipLevel, 10),
        new("함종", row => row.ShipTypeLabel, 12),
        new("진영", row => row.NationLabel, 12),
        new("장전", row => row.ShipReload, 10),
        new("슬롯", row => row.SlotLabel, 10),
        new("장비ID", row => row.EquipmentId, 12),
        new("장비", row => row.EquipmentName, 34),
        new("장비등급", row => row.EquipmentRarity, 12),
        new("종류", row => row.EquipmentTypeLabel, 12),
        new("강화", row => row.EnhanceLevel, 8),
        new("무기ID", row => row.WeaponIds, 18),
        new("무기수", row => row.WeaponCount, 10),
        new("최대장전", row => row.ReloadMax, 10),
        new("쿨다운", row => row.ReloadSeconds, 10),
        new("사속", row => row.FireRatePerMinute, 10),
        new("적용장전", row => row.EffectiveLoadSpeed, 10),
        new("공격역할", row => row.AttackRole, 12),
        new("공격타이밍", row => row.AttackTimingSeconds, 12),
        new("피해", row => row.DamagePerCycle, 10),
        new("DPS", row => row.EstimatedDps, 10),
        new("보정", row => row.SkillAdjustment, 26),
        new("상세", row => row.TimingDetailText, 42),
        new("노트", row => row.Note, 32)
    ];

    private static IReadOnlyList<ExportColumn<ResourceRow>> ResourceColumns() =>
    [
        new("자원", row => row.Label, 20),
        new("수량", row => row.Count, 14)
    ];

    private static IReadOnlyList<ExportColumn<AcademyRoomRow>> AcademyColumns() =>
    [
        new("교실", row => row.RoomId, 10),
        new("UID", row => row.ShipUid, 13),
        new("함선", row => row.ShipName, 28),
        new("스킬위치", row => row.SkillPosition, 12),
        new("완료시각", row => row.FinishAt, 20),
        new("남은시간", row => row.Remaining, 18),
        new("EXP", row => row.Exp, 12)
    ];

    private static IReadOnlyList<ExportColumn<ResearchQueueRow>> ResearchColumns() =>
    [
        new("순서", row => row.Order, 10),
        new("ID", row => row.Id, 16),
        new("완료시각", row => row.FinishAt, 20),
        new("남은시간", row => row.Remaining, 18)
    ];

    private static IReadOnlyList<ExportColumn<CommissionRow>> CommissionColumns() =>
    [
        new("ID", row => row.Id, 16),
        new("완료시각", row => row.FinishAt, 20),
        new("남은시간", row => row.Remaining, 18),
        new("함선", row => row.ShipNames, 42)
    ];

    private static IReadOnlyList<ExportColumn<BuildQueueRow>> BuildColumns() =>
    [
        new("슬롯", row => row.Slot, 10),
        new("상태", row => row.StatusLabel, 14),
        new("시작시각", row => row.StartedAt, 20),
        new("완료시각", row => row.FinishAt, 20),
        new("남은시간", row => row.Remaining, 18),
        new("결과", row => row.ResultName, 24)
    ];

    private static WorkbookSheet BuildSheet<T>(
        string name,
        IEnumerable<T> source,
        IReadOnlyList<ExportColumn<T>> columns)
    {
        var rows = source
            .Select(item => columns.Select(column => column.Value(item)).ToArray())
            .ToArray();
        var workbookColumns = columns
            .Select(column => new WorkbookColumn(column.Header, column.Width))
            .ToArray();

        return new WorkbookSheet(name, workbookColumns, rows);
    }

    private static void WriteContentTypes(ZipArchive archive, int worksheetCount)
    {
        using var writer = CreateXmlWriter(CreateEntryStream(archive, "[Content_Types].xml"));
        writer.WriteStartDocument();
        writer.WriteStartElement("Types", ContentTypeNamespace);
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", "rels");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", "xml");
        writer.WriteAttributeString("ContentType", "application/xml");
        writer.WriteEndElement();
        WriteOverride(writer, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        WriteOverride(writer, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        for (var index = 1; index <= worksheetCount; index++)
        {
            WriteOverride(
                writer,
                $"/xl/worksheets/sheet{index.ToString(CultureInfo.InvariantCulture)}.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteRootRelationships(ZipArchive archive)
    {
        using var writer = CreateXmlWriter(CreateEntryStream(archive, "_rels/.rels"));
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", RelationshipNamespace);
        WriteRelationship(
            writer,
            "rId1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
            "xl/workbook.xml");
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWorkbook(ZipArchive archive, IReadOnlyList<WorkbookSheet> sheets)
    {
        using var writer = CreateXmlWriter(CreateEntryStream(archive, "xl/workbook.xml"));
        writer.WriteStartDocument();
        writer.WriteStartElement("workbook", SpreadsheetNamespace);
        writer.WriteAttributeString("xmlns", "r", null, OfficeRelationshipNamespace);
        writer.WriteStartElement("sheets");
        for (var index = 0; index < sheets.Count; index++)
        {
            var sheetId = index + 1;
            writer.WriteStartElement("sheet");
            writer.WriteAttributeString("name", SafeSheetName(sheets[index].Name));
            writer.WriteAttributeString("sheetId", sheetId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("r", "id", OfficeRelationshipNamespace, $"rId{sheetId.ToString(CultureInfo.InvariantCulture)}");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWorkbookRelationships(ZipArchive archive, int worksheetCount)
    {
        using var writer = CreateXmlWriter(CreateEntryStream(archive, "xl/_rels/workbook.xml.rels"));
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", RelationshipNamespace);
        for (var index = 1; index <= worksheetCount; index++)
        {
            WriteRelationship(
                writer,
                $"rId{index.ToString(CultureInfo.InvariantCulture)}",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                $"worksheets/sheet{index.ToString(CultureInfo.InvariantCulture)}.xml");
        }

        WriteRelationship(
            writer,
            $"rId{(worksheetCount + 1).ToString(CultureInfo.InvariantCulture)}",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles",
            "styles.xml");
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteStyles(ZipArchive archive)
    {
        using var writer = CreateXmlWriter(CreateEntryStream(archive, "xl/styles.xml"));
        writer.WriteStartDocument();
        writer.WriteStartElement("styleSheet", SpreadsheetNamespace);

        writer.WriteStartElement("fonts");
        writer.WriteAttributeString("count", "2");
        WriteFont(writer, false);
        WriteFont(writer, true);
        writer.WriteEndElement();

        writer.WriteStartElement("fills");
        writer.WriteAttributeString("count", "3");
        WriteFill(writer, "none", null);
        WriteFill(writer, "gray125", null);
        WriteFill(writer, "solid", "FF1F6FEB");
        writer.WriteEndElement();

        writer.WriteStartElement("borders");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("border");
        writer.WriteStartElement("left");
        writer.WriteEndElement();
        writer.WriteStartElement("right");
        writer.WriteEndElement();
        writer.WriteStartElement("top");
        writer.WriteEndElement();
        writer.WriteStartElement("bottom");
        writer.WriteEndElement();
        writer.WriteStartElement("diagonal");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("cellStyleXfs");
        writer.WriteAttributeString("count", "1");
        WriteXf(writer, 0, 0);
        writer.WriteEndElement();

        writer.WriteStartElement("cellXfs");
        writer.WriteAttributeString("count", "2");
        WriteXf(writer, 0, 0);
        WriteXf(writer, 1, 2, true);
        writer.WriteEndElement();

        writer.WriteStartElement("cellStyles");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("cellStyle");
        writer.WriteAttributeString("name", "Normal");
        writer.WriteAttributeString("xfId", "0");
        writer.WriteAttributeString("builtinId", "0");
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWorksheet(ZipArchive archive, WorkbookSheet sheet, int sheetIndex)
    {
        using var writer = CreateXmlWriter(CreateEntryStream(
            archive,
            $"xl/worksheets/sheet{sheetIndex.ToString(CultureInfo.InvariantCulture)}.xml"));
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);

        WriteSheetViews(writer);
        WriteColumns(writer, sheet.Columns);

        writer.WriteStartElement("sheetData");
        WriteRow(writer, 1, sheet.Columns.Select(column => column.Header), true);
        for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
        {
            WriteRow(writer, rowIndex + 2, sheet.Rows[rowIndex], false);
        }

        writer.WriteEndElement();
        WriteAutoFilter(writer, sheet);
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteSheetViews(XmlWriter writer)
    {
        writer.WriteStartElement("sheetViews");
        writer.WriteStartElement("sheetView");
        writer.WriteAttributeString("workbookViewId", "0");
        writer.WriteStartElement("pane");
        writer.WriteAttributeString("ySplit", "1");
        writer.WriteAttributeString("topLeftCell", "A2");
        writer.WriteAttributeString("activePane", "bottomLeft");
        writer.WriteAttributeString("state", "frozen");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteColumns(XmlWriter writer, IReadOnlyList<WorkbookColumn> columns)
    {
        writer.WriteStartElement("cols");
        for (var index = 0; index < columns.Count; index++)
        {
            var columnIndex = index + 1;
            var width = Math.Clamp(columns[index].Width, 8, 60);
            writer.WriteStartElement("col");
            writer.WriteAttributeString("min", columnIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("max", columnIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("width", width.ToString("0.##", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteRow(XmlWriter writer, int rowIndex, IEnumerable<object?> values, bool isHeader)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", rowIndex.ToString(CultureInfo.InvariantCulture));
        var columnIndex = 1;
        foreach (var value in values)
        {
            WriteCell(writer, rowIndex, columnIndex, value, isHeader);
            columnIndex++;
        }

        writer.WriteEndElement();
    }

    private static void WriteCell(XmlWriter writer, int rowIndex, int columnIndex, object? value, bool isHeader)
    {
        writer.WriteStartElement("c");
        writer.WriteAttributeString("r", $"{ColumnName(columnIndex)}{rowIndex.ToString(CultureInfo.InvariantCulture)}");
        if (isHeader)
        {
            writer.WriteAttributeString("s", "1");
        }

        if (!isHeader && TryWriteNumericCellValue(writer, value))
        {
            writer.WriteEndElement();
            return;
        }

        writer.WriteAttributeString("t", "inlineStr");
        writer.WriteStartElement("is");
        writer.WriteStartElement("t");
        writer.WriteString(SafeText(value));
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static bool TryWriteNumericCellValue(XmlWriter writer, object? value)
    {
        var text = value switch
        {
            byte typed => typed.ToString(CultureInfo.InvariantCulture),
            sbyte typed => typed.ToString(CultureInfo.InvariantCulture),
            short typed => typed.ToString(CultureInfo.InvariantCulture),
            ushort typed => typed.ToString(CultureInfo.InvariantCulture),
            int typed => typed.ToString(CultureInfo.InvariantCulture),
            uint typed => typed.ToString(CultureInfo.InvariantCulture),
            long typed => typed.ToString(CultureInfo.InvariantCulture),
            ulong typed => typed.ToString(CultureInfo.InvariantCulture),
            float typed when float.IsFinite(typed) => typed.ToString("G", CultureInfo.InvariantCulture),
            double typed when double.IsFinite(typed) => typed.ToString("G", CultureInfo.InvariantCulture),
            decimal typed => typed.ToString(CultureInfo.InvariantCulture),
            _ => null
        };

        if (text is null)
        {
            return false;
        }

        writer.WriteStartElement("v");
        writer.WriteString(text);
        writer.WriteEndElement();
        return true;
    }

    private static void WriteAutoFilter(XmlWriter writer, WorkbookSheet sheet)
    {
        if (sheet.Columns.Count == 0)
        {
            return;
        }

        var lastColumn = ColumnName(sheet.Columns.Count);
        var lastRow = Math.Max(1, sheet.Rows.Count + 1);
        writer.WriteStartElement("autoFilter");
        writer.WriteAttributeString("ref", $"A1:{lastColumn}{lastRow.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteEndElement();
    }

    private static void WriteOverride(XmlWriter writer, string partName, string contentType)
    {
        writer.WriteStartElement("Override");
        writer.WriteAttributeString("PartName", partName);
        writer.WriteAttributeString("ContentType", contentType);
        writer.WriteEndElement();
    }

    private static void WriteRelationship(XmlWriter writer, string id, string type, string target)
    {
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", id);
        writer.WriteAttributeString("Type", type);
        writer.WriteAttributeString("Target", target);
        writer.WriteEndElement();
    }

    private static void WriteFont(XmlWriter writer, bool header)
    {
        writer.WriteStartElement("font");
        if (header)
        {
            writer.WriteStartElement("b");
            writer.WriteEndElement();
        }

        writer.WriteStartElement("sz");
        writer.WriteAttributeString("val", "11");
        writer.WriteEndElement();
        writer.WriteStartElement("color");
        writer.WriteAttributeString(header ? "rgb" : "theme", header ? "FFFFFFFF" : "1");
        writer.WriteEndElement();
        writer.WriteStartElement("name");
        writer.WriteAttributeString("val", "Calibri");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteFill(XmlWriter writer, string patternType, string? foregroundColor)
    {
        writer.WriteStartElement("fill");
        writer.WriteStartElement("patternFill");
        writer.WriteAttributeString("patternType", patternType);
        if (!string.IsNullOrWhiteSpace(foregroundColor))
        {
            writer.WriteStartElement("fgColor");
            writer.WriteAttributeString("rgb", foregroundColor);
            writer.WriteEndElement();
            writer.WriteStartElement("bgColor");
            writer.WriteAttributeString("indexed", "64");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteXf(XmlWriter writer, int fontId, int fillId, bool applyHeaderStyle = false)
    {
        writer.WriteStartElement("xf");
        writer.WriteAttributeString("numFmtId", "0");
        writer.WriteAttributeString("fontId", fontId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fillId", fillId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("borderId", "0");
        writer.WriteAttributeString("xfId", "0");
        if (applyHeaderStyle)
        {
            writer.WriteAttributeString("applyFont", "1");
            writer.WriteAttributeString("applyFill", "1");
        }

        writer.WriteEndElement();
    }

    private static Stream CreateEntryStream(ZipArchive archive, string path)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        return entry.Open();
    }

    private static XmlWriter CreateXmlWriter(Stream stream) =>
        XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            CloseOutput = true
        });

    private static string ColumnName(int index)
    {
        var name = "";
        while (index > 0)
        {
            var modulo = (index - 1) % 26;
            name = Convert.ToChar('A' + modulo, CultureInfo.InvariantCulture) + name;
            index = (index - modulo) / 26;
        }

        return name;
    }

    private static string SafeSheetName(string name)
    {
        var invalidCharacters = new HashSet<char> { '[', ']', ':', '*', '?', '/', '\\' };
        var safe = new string(name.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
        safe = string.IsNullOrWhiteSpace(safe) ? "Sheet" : safe.Trim('\'');
        return safe.Length <= 31 ? safe : safe[..31];
    }

    private static string SafeText(object? value)
    {
        if (value is null)
        {
            return "";
        }

        var text = value switch
        {
            bool typed => YesNo(typed),
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? ""
        };

        if (text.Length > 32767)
        {
            text = text[..32764] + "...";
        }

        if (text.All(XmlConvert.IsXmlChar))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (XmlConvert.IsXmlChar(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string StatValue(ShipRow ship, string label) =>
        ship.StatRows.FirstOrDefault(row => string.Equals(row.Label, label, StringComparison.Ordinal))?.ValueText ?? "";

    private static object? StatNumber(ShipRow ship, string label)
    {
        var value = StatValue(ship, label);
        return TryParseLeadingNumber(value, out var number) ? number : null;
    }

    private static object? PositiveOrBlank(int value) => value > 0 ? value : null;

    private static bool TryParseLeadingNumber(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var builder = new StringBuilder();
        var started = false;
        foreach (var character in text.TrimStart())
        {
            if (char.IsDigit(character))
            {
                builder.Append(character);
                started = true;
                continue;
            }

            if (started && character == ',')
            {
                continue;
            }

            if (!started && (character == '+' || char.IsWhiteSpace(character)))
            {
                continue;
            }

            break;
        }

        return builder.Length > 0 &&
               int.TryParse(builder.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string Rarity(string tier, string rarity) => string.IsNullOrWhiteSpace(tier) ? rarity : tier;

    private static string YesNo(bool value) => value ? "Y" : "N";

    private sealed record WorkbookSheet(
        string Name,
        IReadOnlyList<WorkbookColumn> Columns,
        IReadOnlyList<object?[]> Rows);

    private sealed record WorkbookColumn(string Header, double Width);

    private sealed record ExportColumn<T>(string Header, Func<T, object?> Value, double Width = 14);
}
