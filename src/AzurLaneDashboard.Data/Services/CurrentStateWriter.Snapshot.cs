using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    private StateApplyResult ApplySnapshotPacket(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        return packet.PacketId switch
        {
            11003 => ApplyCommander(tx, packet, json),
            11004 => ApplyResources(tx, packet, json),
            12001 or 12010 or 12026 or 12030 or 12042 => ApplyShips(tx, packet, json),
            12101 => ApplyFleetSnapshot(tx, packet, json),
            12106 => ApplyFleetUpdate(tx, packet, json),
            12024 => ApplyBuilds(tx, packet, json),
            13002 or 13010 => ApplyCommissions(tx, packet, json),
            13011 => ApplyCommissionUpdate(tx, packet, json),
            14001 => ApplyEquipmentSnapshot(tx, packet, json),
            14101 => ApplyEquipmentSkins(tx, packet, json),
            14200 => ApplySpWeaponSnapshot(tx, packet, json),
            15001 => ApplyItems(tx, packet, json),
            22001 => ApplyAcademy(tx, packet, json),
            22013 => ApplyAcademyStatus(tx, packet, json),
            25001 or 25039 => ApplyCommanderSnapshot(tx, packet, json),
            33001 or 33601 => ApplyWorldSnapshot(tx, packet, json),
            20001 or 20002 or 20003 or 20004 or 20012 => ApplyTaskSnapshot(tx, packet, json),
            63000 => ApplyResearch(tx, packet, json),
            64000 => ApplyFleetTechSnapshot(tx, packet, json),
            _ => StateApplyResult.Ignored("no snapshot handler", packet.DecodedRowId)
        };
    }
}
