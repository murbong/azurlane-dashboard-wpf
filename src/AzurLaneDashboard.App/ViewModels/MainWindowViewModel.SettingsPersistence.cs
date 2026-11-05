using System.IO;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Data.Services;
using Microsoft.Win32;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string SettingsPath => settingsService.SettingsPath;
    private string settingsSaveError = "";
    public string SettingsSaveError
    {
        get => settingsSaveError;
        private set => SetProperty(ref settingsSaveError, value);
    }

    private void BrowseDatabase()
    {
        var initialDirectory = Path.GetDirectoryName(DatabasePath);
        var dialog = new OpenFileDialog
        {
            Title = "inventory.sqlite 선택",
            Filter = "SQLite DB (*.sqlite;*.db)|*.sqlite;*.db|All files (*.*)|*.*",
            FileName = Path.GetFileName(DatabasePath),
            InitialDirectory = Directory.Exists(initialDirectory)
                ? initialDirectory
                : ProjectLocator.FindProjectRoot()
        };

        if (dialog.ShowDialog() == true)
        {
            DatabasePath = dialog.FileName;
            _ = ReloadAsync();
        }
    }

    private void SaveSettings()
    {
        if (isDisposed)
        {
            return;
        }

        settingsSaveTimer.Stop();
        settingsSaveTimer.Start();
    }

    private void SettingsSaveTimerTick(object? sender, EventArgs e)
    {
        settingsSaveTimer.Stop();
        SaveSettingsNow();
    }

    private void SaveSettingsNow()
    {
        try
        {
            settingsService.Save(new AppSettings
            {
                DatabasePath = DatabasePath,
                GrowthGoals = GrowthPlanner.SavedGoals,
                IsDarkMode = IsDarkMode,
                IsAutoRefreshEnabled = IsAutoRefreshEnabled,
                IsLiveStateExportEnabled = IsLiveStateExportEnabled,
                AlautoRoot = Macros.AlautoRoot,
                NotifyMacroEnd = Macros.NotifyOnEnd,
                NotifySortieEnd = NotifySortieEnd,
                RarityFilter = SelectedRarityFilter,
                DockShipSortOption = selectedDockShipSortOption,
                LevelShipSortOption = selectedLevelShipSortOption,
                FleetDamageSortOption = selectedFleetDamageSortOption,
                DockShipSortAscending = isDockShipSortAscending,
                LevelShipSortAscending = isLevelShipSortAscending,
                FleetDamageSortAscending = isFleetDamageSortAscending,
                ShipTypeFilter = SelectedShipTypeFilter,
                NationFilter = SelectedNationFilter,
                ShipExtraFilter = SelectedShipExtraFilter,
                EquipmentSortOption = SelectedEquipmentSortOption,
                EquipmentSortAscending = isEquipmentSortAscending,
                EquipmentTypeFilter = SelectedEquipmentTypeFilter,
                EquipmentExtraFilter = SelectedEquipmentExtraFilter,
                SelectedMainTabIndex = SelectedMainTabIndex,
                LevelingDriverCount = FleetPlanner.DriverOverride,
                LevelingStageKey = FleetPlanner.SelectedStageKey,
                MetaBossTemplateId = FleetPlanner.SavedBossId,
                GearSourceMode = FleetPlanner.GearModeKey,
                Region = Region,
                Serial = Serial,
                CaptureSerials = captureSerials.ToList(),
                AdbPath = AdbPath,
                MuMuManagerPath = MuMuManagerPath,
                DataDirectory = DataDirectory,
                CaptureBackend = CaptureBackend,
                CaptureInterface = CaptureInterface,
                CaptureDumpcapPath = CaptureDumpcapPath,
                CaptureServerPort = CaptureServerPort,
                CaptureServerHost = CaptureServerHost,
                CaptureStatusInterval = CaptureStatusInterval,
                CaptureVerbose = CaptureVerbose,
                GuildDockCapacityBonus = GuildDockCapacityBonus,
                LuaRepoDirectory = LuaRepoDirectory,
                LuaBranch = LuaBranch,
                MapsDirectory = MapsDirectory,
                GameResourcesDirectory = GameResourcesDirectory,
                StaticAssetsDirectory = StaticAssetsDirectory,
                LocalAssetManifest = LocalAssetManifest,
                UpdateStatePath = UpdateStatePath,
                Categories = ParseCategories(CategoriesText),
                ProgressEvery = ProgressEvery,
                SkipGit = SkipGit,
                SkipRegal = SkipRegal,
                SkipGameResources = SkipGameResources,
                RequireGameResources = RequireGameResources,
                MetadataOnly = MetadataOnly,
                SkipScripts = SkipScripts,
                ForceGameResources = ForceGameResources,
                SkipIconExtract = SkipIconExtract,
                ForceIconExtract = ForceIconExtract
            });
            SettingsSaveError = "";
        }
        catch (Exception ex)
        {
            SettingsSaveError = $"설정과 육성 목표를 저장하지 못했습니다: {ex.Message}";
        }
    }
}
