using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static void AttachCommunityRatings(List<ShipState> ships, JsonElement regalShips)
    {
        if (regalShips.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var regalIndex = BuildRegalShipIndex(regalShips);
        foreach (var ship in ships)
        {
            var regal = MatchRegalShip(ship, regalIndex);
            if (regal.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var ratings = Child(regal, "ratings");
            var best = BestRegalRating(ratings);
            if (string.IsNullOrWhiteSpace(best.Rating))
            {
                continue;
            }

            ship.CommunityLabel = $"{best.Context} {best.Rating}".Trim();
            ship.CommunitySummary = FirstNonEmpty(JsonString(regal, "desc"), JsonString(regal, "name"));
            ship.CommunityScore = RegalShipScore(ratings);
            ship.CommunityBestScore = best.Score;
            ship.CommunityRatings = RegalRatings(ratings);
        }
    }

    private static List<ShipCommunityRatingRow> RegalRatings(JsonElement ratings)
    {
        if (ratings.ValueKind != JsonValueKind.Object)
        {
            return new List<ShipCommunityRatingRow>();
        }

        var rows = new List<ShipCommunityRatingRow>();
        foreach (var property in ratings.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var rating = property.Value.GetString() ?? "";
            var score = RegalRatingScore(rating);
            if (score > 0)
            {
                rows.Add(new ShipCommunityRatingRow(property.Name, rating, score));
            }
        }

        return rows
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.Context, StringComparer.CurrentCulture)
            .ToList();
    }

    private static int RegalShipScore(JsonElement ratings)
    {
        if (ratings.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var scores = ratings.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .Select(property => RegalRatingScore(property.Value.GetString() ?? ""))
            .Where(score => score > 0)
            .ToList();
        if (scores.Count == 0)
        {
            return 0;
        }

        var best = scores.Max();
        var average = scores.Average();
        return (int)Math.Round(best * 0.65 + average * 0.35, MidpointRounding.AwayFromZero);
    }

    private static (string Context, string Rating, int Score) BestRegalRating(JsonElement ratings)
    {
        if (ratings.ValueKind != JsonValueKind.Object)
        {
            return ("", "", 0);
        }

        var best = ("", "", 0);
        foreach (var property in ratings.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var rating = property.Value.GetString() ?? "";
            var score = RegalRatingScore(rating);
            if (score > best.Item3)
            {
                best = (property.Name, rating, score);
            }
        }

        return best;
    }

    private static int RegalRatingScore(string rating)
    {
        var normalized = rating.Trim().ToUpperInvariant().Replace("*", "", StringComparison.Ordinal);
        return normalized switch
        {
            "EX" => 60,
            "SS" => 52,
            "S+" => 48,
            "S" => 44,
            "A+" => 40,
            "A" => 34,
            "B" => 22,
            "C" => 10,
            "D" => 0,
            _ => 0
        };
    }
}