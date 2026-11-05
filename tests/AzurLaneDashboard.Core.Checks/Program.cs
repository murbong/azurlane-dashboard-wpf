using System.Reflection;
using AzurLaneDashboard.Capture;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;
using AzurLaneDashboard.Protocol;
using Microsoft.Data.Sqlite;

if (args.FirstOrDefault() == "--process-output-fixture")
{
    if (args.Contains("wait"))
    {
        Console.WriteLine(Environment.ProcessId);
        Console.Out.Flush();
        await Task.Delay(TimeSpan.FromMinutes(1));
    }
    else
    {
        var text = new string('x', 4096);
        await Task.WhenAll(
            Task.Run(() => { for (var index = 0; index < 768; index++) Console.Out.Write(text); }),
            Task.Run(() => { for (var index = 0; index < 768; index++) Console.Error.Write(text); }));
    }
    return;
}
await UpdateProcessChecks.RunAsync();
var unknown = AuctionBidCalculator.Calculate("A", 1, 0, 0, 0);
GrowthPlannerChecks.Run();
FleetPlanChecks.Run();
LevelingPlanChecks.Run();
DormPlanChecks.Run();
MetaBossChecks.Run();
MetaFightChecks.Run();
MeowfficerChecks.Run();
GearAllocationChecks.Run();
FleetCompositionChecks.Run();
CurrentStateChecks.Run();
LiveResourceChecks.Run();
ExpReplayChecks.Run();
ShipLifecycleChecks.Run();
SystemsReplayChecks.Run();
EquipmentBagChecks.Run();
ShipDetailChecks.Run();
ShipUpgradeChecks.Run();
RetiredStorageChecks.Run();
MonthlyShopChecks.Run();
await AdbAttachChecks.RunAsync();
SortieRunChecks.Run();
AccountRoutingChecks.Run();
Ensure(unknown.RecommendedBid == 0, "정보가 없을 때는 입찰을 권하지 않아야 합니다.");

var profitable = AuctionBidCalculator.Calculate("B", 1, 600_000, 1_000_000, 0);
Ensure(profitable.RecommendedBid > 0, "가치가 충분하면 양수 입찰가를 제안해야 합니다.");
Ensure(profitable.RecommendedBid <= profitable.BreakEvenCeiling, "추천가는 손익분기 상한을 넘을 수 없습니다.");
Ensure(profitable.WinProbability is >= 0 and <= 1, "승률은 0~1 범위여야 합니다.");

var overpriced = AuctionBidCalculator.Calculate("B", 5, 300_000, 500_000, 900_000);
Ensure(overpriced.RecommendedBid == 0, "관측 입찰가가 가치보다 높으면 관망해야 합니다.");

var item = Message(
    VarintField(1, 1),
    VarintField(2, 1),
    VarintField(3, 5),
    MessageField(4, Message(VarintField(1, 2), VarintField(2, 3))));
var settlement = Message(
    VarintField(1, 77),
    VarintField(2, 1_000_000),
    MessageField(3, item));
var decoded = new GamePayloadDecoder().Decode(23408, settlement);
Ensure(decoded["bid_price"]?.GetValue<long>() == 1_000_000, "23408 낙찰가를 이름 있는 필드로 디코딩해야 합니다.");
Ensure(decoded["item_list"]?[0]?["id"]?.GetValue<long>() == 1, "23408 물품 ID를 중첩 디코딩해야 합니다.");
Ensure(decoded["item_list"] is System.Text.Json.Nodes.JsonArray { Count: 1 }, "원소가 1개인 repeated 필드도 배열이어야 합니다.");
var match = new GamePayloadDecoder().Decode(23005, Message(VarintField(1, 1), VarintField(2, 107)));
Ensure(match["game_type"]?.GetValue<long>() == 107, "23005에서 S 세션을 식별할 수 있어야 합니다.");
var session = new GamePayloadDecoder().Decode(23418, Message(VarintField(1, 107)));
Ensure(session["type"]?.GetValue<long>() == 107, "23418에서 S 세션을 식별할 수 있어야 합니다.");
var dormLogin = new GamePayloadDecoder().Decode(19001, Message(
    VarintField(2, 20000),
    MessageField(5, Message(VarintField(1, 101), VarintField(2, 1))),
    MessageField(5, Message(VarintField(1, 102), VarintField(2, 2))),
    VarintField(10, 1_800_000_000)));
Ensure(dormLogin["ship_list"]?[1]?["floor"]?.GetValue<long>() == 2 && dormLogin["next_timestamp"]?.GetValue<long>() == 1_800_000_000,
    "19001 숙소 함선 목록을 함선 ID와 층으로 디코딩해야 합니다.");
var shipList = new GamePayloadDecoder().Decode(12001, Message(
    MessageField(1, Message(VarintField(1, 201), VarintField(3, 115), VarintField(19, 120), VarintField(20, 1),
        MessageField(23, Message(VarintField(1, 900), VarintField(2, 2001))),
        MessageField(24, Message(VarintField(1, 1), VarintField(2, 499081)))))));
Ensure(shipList["shiplist"]?[0]?["max_level"]?.GetValue<long>() == 120 && shipList["shiplist"]?[0]?["common_flag"]?.GetValue<long>() == 1,
    "함선 정보 필드 19는 최대 레벨, 20은 common_flag로 읽어야 합니다.");
Ensure(shipList["shiplist"]?[0]?["spweapon"]?["template_id"]?.GetValue<long>() == 2001 &&
       shipList["shiplist"]?[0]?["skin_shadow_list"]?[0]?["value"]?.GetValue<long>() == 499081 &&
       shipList["shiplist"]?[0]?["meta_repair_list"] is null,
    "함선 정보 필드 23은 장착 특수장비, 24는 skin_shadow_list로 읽어야 합니다.");
var dormSettlement = new GamePayloadDecoder().Decode(19027, Message(VarintField(1, 10), VarintField(2, 360), VarintField(3, 1_800_000_100)));
Ensure(dormSettlement["food"]?.GetValue<long>() == 360, "19027 숙소 정산의 식량 소모량을 디코딩해야 합니다.");

var buildAdbCommand = typeof(DumpcapCaptureService).GetMethod(
    "BuildAdbCommand", BindingFlags.NonPublic | BindingFlags.Static);
var captureCommand = (List<string>?)buildAdbCommand?.Invoke(null,
[
    new CaptureOptions
    {
        AdbPath = "adb",
        Serial = "127.0.0.1:16384",
        ServerPort = 80
    },
    null
]);
Ensure(captureCommand?.Contains("sh") == true, "ADB tcpdump는 원격 셸에서 실행해야 합니다.");
Ensure(captureCommand?.Contains("exec tcpdump \"$@\" 2>/dev/null") == true,
    "원격 tcpdump 진단 문구가 pcap 스트림에 섞이지 않아야 합니다.");

var captureService = new DumpcapCaptureService();
var processField = typeof(DumpcapCaptureService).GetField(
    "process", BindingFlags.NonPublic | BindingFlags.Instance);
using (var detachedProcess = new System.Diagnostics.Process())
{
    processField?.SetValue(captureService, detachedProcess);
    Ensure(!captureService.Status.Running, "연결되지 않았거나 정리된 프로세스 상태 조회가 앱을 종료하면 안 됩니다.");
}

var databasePath = Path.Combine(Path.GetTempPath(), $"azurlane-auction-check-{Guid.NewGuid():N}.sqlite");
try
{
    using (var connection = new SqliteConnection($"Data Source={databasePath}"))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE decoded_packets (
                                  source TEXT, stream_id TEXT, direction TEXT, packet_id INTEGER,
                                  packet_name TEXT, "index" INTEGER, json TEXT
                              );
                              INSERT INTO decoded_packets
                                  (source, stream_id, direction, packet_id, packet_name, "index", json)
                              VALUES
                                  ('check', '1', 'CS', 23418, 'CS_23418', 0, '{"type":107}'),
                                  ('check', '1', 'SC', 23401, 'SC_23401', 0,
                                   '{"round":2,"public_event_effect":{"event_id":501,"item_list":{"uid":1,"id":1}}}');
                              """;
        command.ExecuteNonQuery();
    }

    var snapshot = new NativeSnapshotService().Load(databasePath);
    Ensure(snapshot.Auction.Round == 2, "경매 라운드를 SQLite 스냅샷에 반영해야 합니다.");
    Ensure(snapshot.Auction.Session == "S", "23418 type 107을 S 세션으로 반영해야 합니다.");
    Ensure(snapshot.Auction.Items.Any(row => row.Uid == 1), "공개 물품을 경매 탭 목록에 반영해야 합니다.");
    Ensure(snapshot.Auction.Items.Any(row => row.Uid == 0), "미공개 물품의 세션 기대값을 합계 행으로 표시해야 합니다.");
    Ensure(snapshot.Auction.ExpectedValue == 4_172_500, "S 세션의 총 사전 기대값을 반영해야 합니다.");
}
finally
{
    SqliteConnection.ClearAllPools();
    if (File.Exists(databasePath))
    {
        File.Delete(databasePath);
    }
}

Console.WriteLine("Auction calculator checks passed.");

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static byte[] Message(params byte[][] fields) => fields.SelectMany(field => field).ToArray();

static byte[] VarintField(int number, ulong value) => Message(Varint((ulong)(number << 3)), Varint(value));

static byte[] MessageField(int number, byte[] value) =>
    Message(Varint((ulong)((number << 3) | 2)), Varint((ulong)value.Length), value);

static byte[] Varint(ulong value)
{
    var bytes = new List<byte>();
    do
    {
        var next = (byte)(value & 0x7f);
        value >>= 7;
        bytes.Add(value == 0 ? next : (byte)(next | 0x80));
    } while (value != 0);

    return bytes.ToArray();
}
