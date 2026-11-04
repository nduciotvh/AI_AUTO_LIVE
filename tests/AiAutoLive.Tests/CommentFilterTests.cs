using AiAutoLive;

public class CommentFilterTests
{
    readonly CommentFilter f = new(() => "page1");

    static Comment C(string authorId, string message) =>
        new(Guid.NewGuid().ToString(), authorId, "Lan", message, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Accepts_normal_question() =>
        Assert.Null(f.RejectReason(C("u1", "Áo này còn size M không?")));

    [Fact]
    public void Rejects_page_own_comment() =>
        Assert.Equal("comment của Page", f.RejectReason(C("page1", "Chào cả nhà")));

    [Theory]
    [InlineData("")]
    [InlineData("hi")]
    [InlineData("❤️❤️❤️")]
    [InlineData("...")]
    [InlineData("👍 👍")]
    public void Rejects_fewer_than_three_letters_or_digits(string message) =>
        Assert.Equal("quá ngắn", f.RejectReason(C("u1", message)));

    [Fact]
    public void Accepts_exactly_three_letters() =>
        Assert.Null(f.RejectReason(C("u1", "giá?")));

    [Fact]
    public void Rejects_repeat_of_same_authors_previous_comment()
    {
        Assert.Null(f.RejectReason(C("u1", "Giá bao nhiêu")));
        Assert.Equal("trùng comment trước", f.RejectReason(C("u1", "  giá   BAO nhiêu ")));
    }

    [Fact]
    public void Same_text_from_another_author_is_accepted()
    {
        Assert.Null(f.RejectReason(C("u1", "Giá bao nhiêu")));
        Assert.Null(f.RejectReason(C("u2", "Giá bao nhiêu")));
    }

    [Fact]
    public void Only_immediately_previous_comment_counts()
    {
        Assert.Null(f.RejectReason(C("u1", "Giá bao nhiêu")));
        Assert.Null(f.RejectReason(C("u1", "Ship Hà Nội không")));
        Assert.Null(f.RejectReason(C("u1", "Giá bao nhiêu")));
    }

    [Fact]
    public void Unknown_author_skips_duplicate_check()
    {
        Assert.Null(f.RejectReason(C("", "Giá bao nhiêu")));
        Assert.Null(f.RejectReason(C("", "Giá bao nhiêu")));
    }
}
