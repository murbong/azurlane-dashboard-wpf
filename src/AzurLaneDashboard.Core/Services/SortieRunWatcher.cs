using System.Text.Json;

namespace AzurLaneDashboard.Core.Services;

/// <summary>A repeated sortie that ended or stalled, for the Windows notification.</summary>
public sealed record SortieRunNotice(string Title, string Detail, bool IsWarning);

/// <summary>
/// Follows the game's 연속 작전 (repeat sorties) from captured chapter packets. The client counts the repeats itself
/// and tells the server nothing about them, so a run is read from its shape: each clear (CS 13103 act 0) is followed
/// within seconds by the next entry (13101/13102). A run ends when a clear is not followed by an entry, and it stalls
/// when nothing moves inside a map for minutes (a full dock or low morale stops it there). A single sortie is not a
/// run, so playing a map by hand once does not notify.
/// </summary>
public sealed class SortieRunWatcher
{
    /// <summary>Longest wait between a clear and the next entry within a run; the next clear's notice waits this long.</summary>
    public static readonly TimeSpan ReentryWindow = TimeSpan.FromSeconds(60);

    /// <summary>No move or battle settlement inside a map for this long means the run stopped there.</summary>
    public static readonly TimeSpan StallAfter = TimeSpan.FromMinutes(5);

    private int pendingChapterId;
    private int chapterId;
    private int entries;
    private int clears;
    private bool inChapter;
    private bool lastExitCleared;
    private int rejectedResult;
    private DateTime runStartedAt;
    private DateTime lastExitAt;
    private DateTime lastActivityAt;
    private bool endNotified;
    private bool stallNotified;

    /// <summary>Chapter (13101–13106) and battle (40001–40004) packets; everything else can be skipped.</summary>
    public static bool IsRelevant(int packetId) => packetId is >= 13101 and <= 13106 or >= 40001 and <= 40004;

    /// <summary>Entries in the current run so far; 0 before any.</summary>
    public int Entries => entries;

    public int ChapterId => chapterId;

    public void Observe(int packetId, string direction, JsonElement packet, DateTime at)
    {
        var request = direction.Equals("CS", StringComparison.OrdinalIgnoreCase);
        switch (packetId)
        {
            case 13101 when request:
                pendingChapterId = Int(packet, "id");
                return;
            case 13102 when !request:
                Entered(packet, at);
                return;
            // act 0 leaves the map; act_arg_1 is 1 when it was cleared.
            case 13103 when request && Int(packet, "act") == 0:
                if (!inChapter) return;
                inChapter = false;
                lastExitCleared = Int(packet, "act_arg_1") == 1;
                if (lastExitCleared) clears++;
                lastExitAt = at;
                lastActivityAt = at;
                return;
        }

        // Only moves and battle settlements inside the map count. The game lets the player go back to the lobby
        // without leaving the map, and battles of other modes would otherwise look like progress.
        if (inChapter && packetId is >= 13103 and <= 13106)
        {
            lastActivityAt = at;
        }
    }

    private void Entered(JsonElement packet, DateTime at)
    {
        var result = Int(packet, "result");
        if (result != 0)
        {
            // Refused (no oil, a full dock): the run ends here; the notice says so.
            rejectedResult = result;
            return;
        }

        var id = packet.TryGetProperty("current_chapter", out var chapter) ? Int(chapter, "id") : 0;
        if (id == 0) id = pendingChapterId;
        var continues = entries > 0 && !inChapter && id == chapterId && at - lastExitAt <= ReentryWindow;
        if (!continues)
        {
            entries = 0;
            clears = 0;
            runStartedAt = at;
        }

        entries++;
        chapterId = id;
        inChapter = true;
        rejectedResult = 0;
        lastActivityAt = at;
        endNotified = false;
        stallNotified = false;
    }

    /// <summary>Call every few seconds; returns a notice once per ended run and once per stall.</summary>
    public SortieRunNotice? Check(DateTime now, string stageLabel)
    {
        if (entries < 2) return null;
        var stage = stageLabel.Length > 0 ? stageLabel : $"해역 {chapterId}";
        if (!inChapter && !endNotified && (rejectedResult != 0 || now - lastExitAt >= ReentryWindow))
        {
            endNotified = true;
            // Already reported as stalled with no sortie since: leaving the map later is the player tidying up.
            if (stallNotified) return null;
            var detail = $"{stage} {entries}회 출격 · {clears}회 클리어 · {lastExitAt:HH:mm} 종료 ({Elapsed(lastExitAt - runStartedAt)})";
            if (rejectedResult != 0)
                return new SortieRunNotice("연속 작전이 멈췄습니다", $"다음 출격이 거절되었습니다 (코드 {rejectedResult}).\n{detail}", true);
            return lastExitCleared
                ? new SortieRunNotice("연속 작전이 끝났습니다", detail, false)
                : new SortieRunNotice("연속 작전이 멈췄습니다", $"마지막 출격은 클리어하지 못하고 나왔습니다.\n{detail}", true);
        }

        if (inChapter && !stallNotified && now - lastActivityAt >= StallAfter)
        {
            stallNotified = true;
            return new SortieRunNotice("연속 작전이 멈춘 것 같습니다",
                $"{stage} {entries}번째 출격에서 {(int)StallAfter.TotalMinutes}분째 진행이 없습니다. 게임 화면을 확인해 주세요.\n" +
                $"지금까지 {clears}회 클리어 · {lastActivityAt:HH:mm} 마지막 진행", true);
        }

        return null;
    }

    private static string Elapsed(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}시간 {span.Minutes}분" : $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))}분";

    private static int Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
}
