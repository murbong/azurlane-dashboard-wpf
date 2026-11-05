using System.IO;
using System.Windows;
using AzurLaneDashboard.App.Services;
using Microsoft.Win32;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task ExportWorkbookAsync()
    {
        if (!CanExportWorkbook())
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "XLSX 내보내기",
            Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
            FileName = $"azurlane-dashboard-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = dialog.FileName;
        var exportData = CreateExportData();
        Status = "XLSX 내보내는 중...";

        try
        {
            await Task.Run(() => DashboardWorkbookExporter.Export(path, exportData)).ConfigureAwait(true);
            Status = $"XLSX 내보내기 완료: {Path.GetFileName(path)}";
            AppDialogService.Show(
                $"저장했습니다.\n\n{path}",
                "XLSX 내보내기",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Status = $"XLSX 내보내기 실패: {ex.Message}";
            AppDialogService.Show(
                $"XLSX 파일을 저장하지 못했습니다.\n\n{ex.Message}",
                "XLSX 내보내기 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool CanExportWorkbook() =>
        !IsReloading &&
        !IsCompactingDatabase &&
        (Ships.Count > 0 ||
         Equipment.Count > 0 ||
         SpecialEquipment.Count > 0 ||
         EquipmentSkins.Count > 0 ||
         Resources.Count > 0);

    private DashboardExportData CreateExportData() => new()
    {
        DatabasePath = DatabasePath,
        LastLoadedAt = LastLoadedAt,
        SnapshotSource = SnapshotSource,
        SnapshotWarning = SnapshotWarning,
        DockUsed = DockUsed,
        DockMax = DockMax,
        DockExemptCount = DockExemptCount,
        EquipmentBagCount = EquipmentBagCount,
        EquipmentBagMax = EquipmentBagMax,
        EquipmentEquippedCount = EquipmentEquippedCount,
        Ships = Ships.ToArray(),
        Equipment = Equipment.ToArray(),
        EquipmentRecommendations = EquipmentRecommendations.ToArray(),
        SpecialEquipment = SpecialEquipment.ToArray(),
        EquipmentSkins = EquipmentSkins.ToArray(),
        LevelPriorities = LevelPriorities.ToArray(),
        GearGaps = GearGaps.ToArray(),
        SkillPriorities = SkillPriorities.ToArray(),
        FleetDamageRows = FleetDamageRows.ToArray(),
        Resources = Resources.ToArray(),
        AcademyRooms = AcademyRooms.ToArray(),
        ResearchQueue = ResearchQueue.ToArray(),
        Commissions = Commissions.ToArray(),
        Builds = Builds.ToArray()
    };
}
