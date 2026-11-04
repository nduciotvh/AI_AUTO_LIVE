using AiAutoLive;

public class CommentPollerTests
{
    static Comment C(string id) => new(id, "u1", "Lan", "Áo còn không?", DateTimeOffset.UnixEpoch);

    [Fact]
    public void First_poll_marks_all_seen_and_returns_none()
    {
        var seen = new HashSet<string>();
        Assert.Empty(CommentPoller.NewComments(seen, new[] { C("3"), C("2"), C("1") }, firstPoll: true));
        Assert.Equal(new[] { "1", "2", "3" }, seen.Order());
    }

    [Fact]
    public void Later_poll_returns_unseen_oldest_first()
    {
        var seen = new HashSet<string> { "1", "2" };
        var fresh = CommentPoller.NewComments(seen, new[] { C("5"), C("4"), C("3"), C("2") }, firstPoll: false);
        Assert.Equal(new[] { "3", "4", "5" }, fresh.Select(c => c.Id));
        Assert.Contains("5", seen);
    }
}
