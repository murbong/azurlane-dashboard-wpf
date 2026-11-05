namespace AzurLaneDashboard.Core.Services;

public static class RarityHelper
{
    public static string Normalize(object? value)
    {
        var key = (value?.ToString() ?? "").Trim().ToLowerInvariant();
        return key switch
        {
            "dr" or "decisive" or "decisive rarity" => "DR",
            "pr" or "priority" or "priority rarity" => "PR",
            "6" or "ur" or "ultra rare" => "UR",
            "5" or "ssr" or "super rare" => "SSR",
            "4" or "sr" or "elite" => "SR",
            "3" or "r" or "rare" => "R",
            "2" or "1" or "n" or "normal" or "common" => "N",
            _ => ""
        };
    }
}
