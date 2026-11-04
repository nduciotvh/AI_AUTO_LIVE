namespace AiAutoLive;

public sealed record PlayItem(string Url, bool IsReply);

/// Decides what the player plays next: background playlist clips with replies slotted in between.
public sealed class PlaybackQueue(IReadOnlyList<string> playlistUrls, int maxRepliesInRow, TimeSpan replyMaxAge)
{
    readonly Lock gate = new();
    readonly Queue<(string Url, DateTimeOffset CreatedAt)> replies = new();
    int cursor;
    int repliesInRow;

    public void AddReply(string url, DateTimeOffset commentCreatedAt)
    {
        lock (gate) replies.Enqueue((url, commentCreatedAt));
    }

    public PlayItem? Next(DateTimeOffset now)
    {
        lock (gate)
        {
            while (replies.TryPeek(out var r) && now - r.CreatedAt > replyMaxAge)
                replies.Dequeue();

            if (replies.Count > 0 && (repliesInRow < maxRepliesInRow || playlistUrls.Count == 0))
            {
                repliesInRow++;
                return new PlayItem(replies.Dequeue().Url, IsReply: true);
            }

            if (playlistUrls.Count == 0) return null;

            var url = playlistUrls[cursor];
            cursor = (cursor + 1) % playlistUrls.Count;
            repliesInRow = 0;
            return new PlayItem(url, IsReply: false);
        }
    }

    public static IReadOnlyList<string> PlaylistUrls(string dir) =>
        Directory.GetFiles(dir, "*.mp3")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .Select(name => "/audio/playlist/" + Uri.EscapeDataString(name!))
            .ToList();
}
