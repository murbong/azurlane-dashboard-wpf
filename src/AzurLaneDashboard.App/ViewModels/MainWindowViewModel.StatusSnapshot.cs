namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string FilteredEquipmentRecommendationText =>
        $"{filteredEquipmentRecommendationCount:N0}/{EquipmentRecommendations.Count:N0}개";

    public string FilteredSpecialEquipmentText =>
        $"{filteredSpecialEquipmentCount:N0}/{SpecialEquipment.Count:N0}개";

    public string FilteredEquipmentSkinText =>
        $"{filteredEquipmentSkinCount:N0}/{EquipmentSkins.Count:N0}개";

    public string FilteredCommunityGearShortageText =>
        $"{filteredCommunityGearShortageCount:N0}/{CommunityGearShortages.Count:N0}개";

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public string SnapshotSource
    {
        get => snapshotSource;
        private set => SetProperty(ref snapshotSource, value);
    }

    public string SnapshotWarning
    {
        get => snapshotWarning;
        private set => SetProperty(ref snapshotWarning, value);
    }

    public string CurrentStateMode
    {
        get => currentStateMode;
        private set => SetProperty(ref currentStateMode, value);
    }

    public int CurrentStateLastDecodedRowId
    {
        get => currentStateLastDecodedRowId;
        private set => SetProperty(ref currentStateLastDecodedRowId, value);
    }

    public string CurrentStateLastStatus
    {
        get => currentStateLastStatus;
        private set => SetProperty(ref currentStateLastStatus, value);
    }

    public string CurrentStateLastArea
    {
        get => currentStateLastArea;
        private set => SetProperty(ref currentStateLastArea, value);
    }

    public string CurrentStateLastDetail
    {
        get => currentStateLastDetail;
        private set => SetProperty(ref currentStateLastDetail, value);
    }

    public string CurrentStateLastError
    {
        get => currentStateLastError;
        private set => SetProperty(ref currentStateLastError, value);
    }

    public string CurrentStateStatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CurrentStateLastError))
            {
                return $"오류 row {CurrentStateLastDecodedRowId:N0}: {CurrentStateLastError}";
            }

            var mode = string.IsNullOrWhiteSpace(CurrentStateMode) ? "-" : CurrentStateMode;
            var statusText = string.IsNullOrWhiteSpace(CurrentStateLastStatus) ? "-" : CurrentStateLastStatus;
            var area = string.IsNullOrWhiteSpace(CurrentStateLastArea) ? "" : $" · {CurrentStateLastArea}";
            return $"{mode} · row {CurrentStateLastDecodedRowId:N0} · {statusText}{area}";
        }
    }

    public int PacketsSeen
    {
        get => packetsSeen;
        private set => SetProperty(ref packetsSeen, value);
    }

    public int KnownPacketsSeen
    {
        get => knownPacketsSeen;
        private set => SetProperty(ref knownPacketsSeen, value);
    }

    public int KnownPacketsDecoded
    {
        get => knownPacketsDecoded;
        private set => SetProperty(ref knownPacketsDecoded, value);
    }

    public int PacketPayloadsSaved
    {
        get => packetPayloadsSaved;
        private set => SetProperty(ref packetPayloadsSaved, value);
    }

    public int DecodeFailuresSaved
    {
        get => decodeFailuresSaved;
        private set => SetProperty(ref decodeFailuresSaved, value);
    }

    public string LiveUpdatedAt
    {
        get => liveUpdatedAt;
        private set => SetProperty(ref liveUpdatedAt, value);
    }

    public string GeneratedAt
    {
        get => generatedAt;
        private set => SetProperty(ref generatedAt, value);
    }
}