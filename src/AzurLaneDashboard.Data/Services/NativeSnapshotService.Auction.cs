using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static AuctionDashboard BuildAuctionDashboard(SqliteConnection connection, JsonElement catalogMap)
    {
        var catalog = ReadAuctionCatalog(catalogMap);
        if (!TableExists(connection, "decoded_packets"))
        {
            return new AuctionDashboard();
        }

        var state = new AuctionState();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT packet_id, json
                              FROM (
                                  SELECT rowid, packet_id, json
                                  FROM decoded_packets
                                  WHERE (direction = 'SC' AND packet_id BETWEEN 23400 AND 23431)
                                     OR (direction = 'CS' AND packet_id IN (23005, 23418))
                                  ORDER BY rowid DESC
                                  LIMIT 1000
                              )
                              ORDER BY rowid
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var packetId = reader.GetInt32(0);
            var json = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                ApplyAuctionPacket(state, packetId, document.RootElement);
            }
            catch (JsonException)
            {
                // A malformed historical row must not hide the rest of the dashboard.
            }
        }

        if (!state.Seen)
        {
            return new AuctionDashboard();
        }

        var rows = state.Items.Values
            .OrderBy(item => item.Uid)
            .Select(item => BuildAuctionItemRow(item, catalog, state.HighestRarity, state.MaximumItemValue))
            .ToList();
        AddHiddenAuctionItems(rows, state);
        var minimum = rows.Sum(row => row.MinimumValue);
        minimum = Math.Max(minimum, state.MaximumItemValue);
        minimum = Math.Max(minimum, state.RarityTotalValues.Values.Sum());
        var expected = rows.Sum(row => row.ExpectedValue);
        var maximum = rows.Sum(row => row.MaximumValue);
        if (state.MaximumCellValue > 0 && state.TotalCellCount > 0)
        {
            maximum = Math.Min(maximum, state.MaximumCellValue * state.TotalCellCount);
        }

        maximum = Math.Max(minimum, maximum);
        expected = Math.Clamp(Math.Max(expected, state.RarityTotalValues.Values.Sum()), minimum, maximum);
        var actual = state.Settled ? rows.Sum(row => row.ExpectedValue) : 0;
        var recommendationSessions = string.IsNullOrWhiteSpace(state.Session)
            ? new[] { "B", "A", "S" }
            : new[] { state.Session };
        var recommendations = recommendationSessions
            .Select(session => AuctionBidCalculator.Calculate(
                session, state.Round, minimum, expected, state.LatestHighBid))
            .ToList();

        var status = state.Settled
            ? $"{state.SessionPrefix}낙찰 결과"
            : state.Round > 0
                ? $"{state.SessionPrefix}진행 중 · {state.Round}라운드"
                : $"{state.SessionPrefix}경매 연결됨";
        var detail = rows.Count == 0
            ? "물품 공개 이벤트를 기다리는 중입니다."
            : $"공개 근거 {state.PublicFactCount:N0}건 · 숨겨진 ID는 추정에 사용하지 않음";

        return new AuctionDashboard
        {
            Status = status,
            Detail = detail,
            Session = state.Session,
            Round = state.Round,
            MinimumValue = minimum,
            ExpectedValue = expected,
            MaximumValue = maximum,
            LatestHighBid = state.LatestHighBid,
            WinningBid = state.WinningBid,
            ActualValue = actual,
            Profit = state.Settled ? actual - state.WinningBid : 0,
            PublicFactCount = state.PublicFactCount,
            Items = new ObservableCollection<AuctionItemRow>(rows),
            Recommendations = new ObservableCollection<AuctionBidRecommendationRow>(recommendations)
        };
    }

    private static void ApplyAuctionPacket(AuctionState state, int packetId, JsonElement json)
    {
        if (packetId is 23005 or 23418)
        {
            var gameType = JsonInt(json, packetId == 23005 ? "game_type" : "type");
            var session = gameType switch
            {
                105 => "B",
                106 => "A",
                107 => "S",
                _ => ""
            };
            if (!string.IsNullOrWhiteSpace(session))
            {
                state.Session = session;
                state.Seen = true;
            }

            return;
        }

        state.Seen = true;
        switch (packetId)
        {
            case 23400:
                state.ResetLot();
                break;
            case 23401:
                state.Round = JsonInt(json, "round");
                ApplyPublicEffect(state, Child(json, "public_event_effect"));
                break;
            case 23403:
                ApplyPublicEffect(state, Child(json, "public_event_effect"));
                break;
            case 23404:
                ApplyPublicEffect(state, Child(json, "event_effect"));
                break;
            case 23407:
                state.LatestHighBid = Elements(json, "bid_list")
                    .Select(item => JsonLong(item, "price"))
                    .DefaultIfEmpty(state.LatestHighBid)
                    .Max();
                break;
            case 23408:
                state.Settled = true;
                state.WinningBid = JsonLong(json, "bid_price");
                state.LatestHighBid = Math.Max(state.LatestHighBid, state.WinningBid);
                ApplySettledItems(state, Elements(json, "item_list"));
                break;
            case 23423:
                state.ResetLot();
                state.Seen = true;
                state.Settled = true;
                ApplySettledItems(state, Elements(json, "item_list"));
                break;
        }
    }

    private static void ApplyPublicEffect(AuctionState state, JsonElement effect)
    {
        if (effect.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var eventId = JsonInt(effect, "event_id");
        if (eventId <= 0)
        {
            return;
        }

        state.PublicFactCount++;
        if (eventId == 901)
        {
            state.MaximumItemValue = JsonLong(effect, "int_value");
        }
        else if (eventId is >= 601 and <= 604)
        {
            state.RarityTotalValues[eventId - 600] = JsonLong(effect, "int_value");
        }
        else if (eventId == 1001)
        {
            state.MaximumCellValue = JsonLong(effect, "int_value");
        }
        else if (eventId == 1101)
        {
            state.HighestRarity = JsonInt(effect, "int_value");
        }
        else if (eventId == 1201)
        {
            state.TotalCellCount = JsonLong(effect, "int_value");
        }

        foreach (var source in Elements(effect, "item_list"))
        {
            var uid = JsonInt(source, "uid");
            if (uid <= 0)
            {
                continue;
            }

            if (!state.Items.TryGetValue(uid, out var item))
            {
                item = new AuctionItemState(uid);
                state.Items[uid] = item;
            }

            if (eventId is 501 or 502)
            {
                item.ItemId = JsonInt(source, "id");
                item.Evidence.Add("물품명 공개");
            }

            if (eventId is 401 or 402)
            {
                item.Rarity = JsonInt(source, "rarity");
                item.Evidence.Add("등급·위치 공개");
            }
            else if (eventId is >= 1701 and <= 1705)
            {
                item.Rarity = eventId - 1700;
                item.Evidence.Add("등급·위치 공개");
            }

            if (eventId is >= 101 and <= 109)
            {
                item.Contour = ContourForEvent(eventId);
                item.Evidence.Add("윤곽 공개");
            }
            else if (eventId == 701)
            {
                item.Contour = ContourFromPositions(source);
                item.Evidence.Add("윤곽 공개");
            }
            else if (eventId == 201)
            {
                item.Evidence.Add("위치 공개");
            }

            if (eventId == 901)
            {
                item.KnownValue = JsonLong(effect, "int_value");
                item.Evidence.Add("감정가 공개");
            }
        }
    }

    private static void ApplySettledItems(AuctionState state, IEnumerable<JsonElement> items)
    {
        state.Items.Clear();
        foreach (var source in items)
        {
            var uid = JsonInt(source, "uid");
            if (uid <= 0)
            {
                uid = state.Items.Count + 1;
            }

            var item = new AuctionItemState(uid)
            {
                ItemId = JsonInt(source, "id"),
                Rarity = JsonInt(source, "rarity"),
                Contour = ContourFromPositions(source)
            };
            item.Evidence.Add("낙찰 후 전체 공개");
            state.Items[uid] = item;
        }
    }

    private static AuctionItemRow BuildAuctionItemRow(
        AuctionItemState item,
        IReadOnlyList<AuctionCatalogItem> catalog,
        int highestRarity,
        long maximumItemValue)
    {
        var exact = item.ItemId > 0
            ? catalog.FirstOrDefault(entry => entry.Id == item.ItemId)
            : null;
        List<AuctionCatalogItem> candidates = exact is not null
            ? [exact]
            : catalog
                .Where(entry => item.Rarity <= 0 || entry.Rarity == item.Rarity)
                .Where(entry => highestRarity <= 0 || entry.Rarity <= highestRarity)
                .Where(entry => maximumItemValue <= 0 || entry.Value <= maximumItemValue)
                .Where(entry => item.Contour is null ||
                                entry.Width == item.Contour.Value.Width &&
                                entry.Height == item.Contour.Value.Height)
                .ToList();

        var minimum = item.KnownValue > 0
            ? item.KnownValue
            : candidates.Count > 0 ? candidates.Min(entry => entry.Value) : 0;
        var expected = item.KnownValue > 0
            ? item.KnownValue
            : candidates.Count > 0 ? (long)Math.Round(candidates.Average(entry => entry.Value)) : 0;
        var maximum = item.KnownValue > 0
            ? item.KnownValue
            : candidates.Count > 0 ? candidates.Max(entry => entry.Value) : 0;

        return new AuctionItemRow
        {
            Uid = item.Uid,
            ItemId = exact?.Id ?? 0,
            Name = exact?.Name ?? "미확인 물품",
            RarityText = item.Rarity > 0 ? $"R{item.Rarity}" : exact is not null ? $"R{exact.Rarity}" : "미공개",
            ContourText = item.Contour is { } contour
                ? $"{contour.Width}×{contour.Height}"
                : exact is not null ? $"{exact.Width}×{exact.Height}" : "미공개",
            Evidence = item.Evidence.Count > 0 ? string.Join(" · ", item.Evidence) : "공개 윤곽",
            MinimumValue = minimum,
            ExpectedValue = expected,
            MaximumValue = maximum,
            IsExact = exact is not null || item.KnownValue > 0
        };
    }

    private static void AddHiddenAuctionItems(
        List<AuctionItemRow> rows,
        AuctionState state)
    {
        if (state.Settled || string.IsNullOrWhiteSpace(state.Session))
        {
            return;
        }

        var prior = AuctionSessionPrior(state.Session);
        var visibleExpected = rows.Sum(row => row.ExpectedValue);
        if (prior.Expected <= visibleExpected)
        {
            return;
        }

        var hiddenExpected = prior.Expected - visibleExpected;
        var visibleMaximum = rows.Sum(row => row.MaximumValue);
        var hiddenMaximum = Math.Max(hiddenExpected, prior.Maximum - visibleMaximum);
        rows.Add(new AuctionItemRow
        {
            Uid = 0,
            Name = "아직 미공개된 물품 합계",
            RarityText = "미공개",
            ContourText = "미공개",
            Evidence = $"{state.Session} 세션 사전 기대값",
            ExpectedValue = hiddenExpected,
            MaximumValue = hiddenMaximum
        });
    }

    private static (long Expected, long Maximum) AuctionSessionPrior(string session)
    {
        (long Low, long High, int Weight)[] ranges = session switch
        {
            "B" => [(0, 300_000, 1000), (300_000, 550_000, 4500), (550_000, 750_000, 4500), (750_000, 1_000_000, 200)],
            "A" => [(0, 700_000, 1600), (700_000, 1_700_000, 4100), (1_700_000, 2_700_000, 4100), (2_700_000, 4_500_000, 200)],
            "S" => [(0, 2_000_000, 1400), (2_000_000, 4_500_000, 4100), (4_500_000, 7_000_000, 4200), (7_000_000, 12_000_000, 300)],
            _ => []
        };
        if (ranges.Length == 0)
        {
            return (0, 0);
        }

        var totalWeight = ranges.Sum(range => range.Weight);
        var expected = ranges.Sum(range => (range.Low + range.High) / 2d * range.Weight) / totalWeight;
        return ((long)Math.Round(expected), ranges.Max(range => range.High));
    }

    private static List<AuctionCatalogItem> ReadAuctionCatalog(JsonElement map)
    {
        var result = new List<AuctionCatalogItem>();
        if (map.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var row in map.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 6)
            {
                continue;
            }

            var cells = row.EnumerateArray().ToArray();
            result.Add(new AuctionCatalogItem(
                IntValue(cells[0]),
                ValueText(cells[1]),
                IntValue(cells[2]),
                cells[3].TryGetInt64(out var value) ? value : 0,
                IntValue(cells[4]),
                IntValue(cells[5])));
        }

        return result;
    }

    private static IEnumerable<JsonElement> Elements(JsonElement parent, string propertyName)
    {
        var child = Child(parent, propertyName);
        return child.ValueKind switch
        {
            JsonValueKind.Array => child.EnumerateArray(),
            JsonValueKind.Object => new[] { child },
            _ => Enumerable.Empty<JsonElement>()
        };
    }

    private static long JsonLong(JsonElement element, string propertyName)
    {
        var value = Child(element, propertyName);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(ValueText(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    private static (int Width, int Height)? ContourForEvent(int eventId)
    {
        var index = eventId - 101;
        return index is >= 0 and < 9 ? (index / 3 + 1, index % 3 + 1) : null;
    }

    private static (int Width, int Height)? ContourFromPositions(JsonElement item)
    {
        var positions = Elements(item, "pos")
            .Select(pos => (X: JsonInt(pos, "x"), Y: JsonInt(pos, "y")))
            .ToList();
        if (positions.Count == 0)
        {
            return null;
        }

        return (
            positions.Max(pos => pos.X) - positions.Min(pos => pos.X) + 1,
            positions.Max(pos => pos.Y) - positions.Min(pos => pos.Y) + 1);
    }

    private sealed record AuctionCatalogItem(int Id, string Name, int Rarity, long Value, int Width, int Height);

    private sealed class AuctionState
    {
        public bool Seen { get; set; }
        public int Round { get; set; }
        public long LatestHighBid { get; set; }
        public long WinningBid { get; set; }
        public bool Settled { get; set; }
        public string Session { get; set; } = "";
        public int HighestRarity { get; set; }
        public long MaximumItemValue { get; set; }
        public long MaximumCellValue { get; set; }
        public long TotalCellCount { get; set; }
        public Dictionary<int, long> RarityTotalValues { get; } = [];
        public int PublicFactCount { get; set; }
        public Dictionary<int, AuctionItemState> Items { get; } = [];
        public string SessionPrefix => string.IsNullOrWhiteSpace(Session) ? "" : $"{Session} · ";

        public void ResetLot()
        {
            Seen = true;
            Round = 0;
            LatestHighBid = 0;
            WinningBid = 0;
            Settled = false;
            HighestRarity = 0;
            MaximumItemValue = 0;
            MaximumCellValue = 0;
            TotalCellCount = 0;
            RarityTotalValues.Clear();
            PublicFactCount = 0;
            Items.Clear();
        }
    }

    private sealed class AuctionItemState(int uid)
    {
        public int Uid { get; } = uid;
        public int ItemId { get; set; }
        public int Rarity { get; set; }
        public long KnownValue { get; set; }
        public (int Width, int Height)? Contour { get; set; }
        public HashSet<string> Evidence { get; } = [];
    }
}
