using AiAutoLive;

public class PlaybackQueueTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    static PlaybackQueue Make(params string[] playlist) =>
        new(playlist, maxRepliesInRow: 2, replyMaxAge: TimeSpan.FromSeconds(90));

    static string[] Take(PlaybackQueue q, int n, DateTimeOffset now) =>
        Enumerable.Range(0, n).Select(_ => q.Next(now)!.Url).ToArray();

    [Fact]
    public void Playlist_loops_in_order() =>
        Assert.Equal(new[] { "a", "b", "a" }, Take(Make("a", "b"), 3, T0));

    [Fact]
    public void At_most_two_replies_in_row_then_one_playlist_clip()
    {
        var q = Make("a", "b");
        q.AddReply("r1", T0); q.AddReply("r2", T0); q.AddReply("r3", T0);
        Assert.Equal(new[] { "r1", "r2", "a", "r3", "b" }, Take(q, 5, T0));
    }

    [Fact]
    public void IsReply_marks_replies()
    {
        var q = Make("a");
        q.AddReply("r1", T0);
        Assert.True(q.Next(T0)!.IsReply);
        Assert.False(q.Next(T0)!.IsReply);
    }

    [Fact]
    public void Reply_older_than_max_age_is_dropped()
    {
        var q = Make("a");
        q.AddReply("old", T0);
        q.AddReply("fresh", T0.AddSeconds(30));
        Assert.Equal(new[] { "fresh", "a" }, Take(q, 2, T0.AddSeconds(91)));
    }

    [Fact]
    public void Reply_exactly_at_max_age_still_plays()
    {
        var q = Make("a");
        q.AddReply("r", T0);
        Assert.Equal("r", q.Next(T0.AddSeconds(90))!.Url);
    }

    [Fact]
    public void Empty_playlist_plays_replies_without_limit_then_null()
    {
        var q = Make();
        q.AddReply("r1", T0); q.AddReply("r2", T0); q.AddReply("r3", T0);
        Assert.Equal(new[] { "r1", "r2", "r3" }, Take(q, 3, T0));
        Assert.Null(q.Next(T0));
    }

    [Fact]
    public void Empty_playlist_with_only_stale_reply_returns_null()
    {
        var q = Make();
        q.AddReply("old", T0);
        Assert.Null(q.Next(T0.AddSeconds(91)));
    }

    [Fact]
    public void PlaylistUrls_takes_mp3_sorted_by_name_and_escaped()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        foreach (var f in new[] { "02 b.mp3", "01-á.mp3", "notes.txt" })
            File.WriteAllText(Path.Combine(dir, f), "");
        Assert.Equal(
            new[] { "/audio/playlist/01-%C3%A1.mp3", "/audio/playlist/02%20b.mp3" },
            PlaybackQueue.PlaylistUrls(dir));
    }
}
