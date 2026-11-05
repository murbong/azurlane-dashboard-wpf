using System.Linq;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ShipRow? SelectedShip
    {
        get => selectedShip;
        set
        {
            if (SetProperty(ref selectedShip, value) && value is not null)
            {
                IsShipDetailOpen = true;
            }
        }
    }

    public EquipmentRow? SelectedEquipment
    {
        get => selectedEquipment;
        set => SetProperty(ref selectedEquipment, value);
    }

    public GearGapRow? SelectedGearGap
    {
        get => selectedGearGap;
        set
        {
            if (SetProperty(ref selectedGearGap, value))
            {
                SelectShipByUid(value?.ShipUid);
            }
        }
    }

    public bool IsShipDetailOpen
    {
        get => isShipDetailOpen;
        private set
        {
            if (SetProperty(ref isShipDetailOpen, value))
            {
                CloseShipDetailCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void SelectShipByUid(string? shipUid)
    {
        var match = ShipByUid(shipUid);
        if (match is not null)
        {
            SelectedShip = match;
        }
    }

    private void OpenShipDetail(object? parameter)
    {
        var ship = SourceShip(parameter);
        if (ship is null)
        {
            return;
        }

        SelectedShip = ship;
        IsShipDetailOpen = true;
    }

    private async void CloseShipDetail()
    {
        var closingShipUid = SelectedShip?.ShipUid;
        IsShipDetailOpen = false;
        await Task.Delay(190);
        if (IsShipDetailOpen || SelectedShip?.ShipUid != closingShipUid)
        {
            return;
        }

        SelectedShip = null;
        SelectedGearGap = null;
    }

    private void RestoreSelections()
    {
        RestoreSelectedShip();
        RestoreSelectedEquipment();
        RestoreSelectedGearGap();
    }

    private void RestoreSelectedShip()
    {
        var selectedUid = SelectedShip?.ShipUid;
        if (string.IsNullOrWhiteSpace(selectedUid))
        {
            return;
        }

        var match = Ships.FirstOrDefault(ship => ship.ShipUid == selectedUid);
        if (match is not null && !ReferenceEquals(match, SelectedShip))
        {
            // Snapshot refresh updates details without reopening a panel being closed.
            selectedShip = match;
            OnPropertyChanged(nameof(SelectedShip));
        }
    }

    private void RestoreSelectedEquipment()
    {
        var templateId = SelectedEquipment?.TemplateId;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return;
        }

        var match = Equipment.FirstOrDefault(item => item.TemplateId == templateId);
        if (match is not null && !ReferenceEquals(match, SelectedEquipment))
        {
            SelectedEquipment = match;
        }
    }

    private void RestoreSelectedGearGap()
    {
        var selectedUid = SelectedGearGap?.ShipUid;
        if (string.IsNullOrWhiteSpace(selectedUid))
        {
            return;
        }

        var match = GearGaps.FirstOrDefault(row => row.ShipUid == selectedUid);
        if (match is not null && !ReferenceEquals(match, SelectedGearGap))
        {
            selectedGearGap = match;
            OnPropertyChanged(nameof(SelectedGearGap));
        }
    }
}
