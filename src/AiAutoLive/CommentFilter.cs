namespace AiAutoLive;

/// AuthorId / AuthorName are "" when Facebook does not reveal who commented.
public sealed record Comment(string Id, string AuthorId, string AuthorName, string Message, DateTimeOffset CreatedAt);

/// Cheap rules that drop comments not worth a GPT call.
public sealed class CommentFilter(string pageId)
{
    readonly Lock gate = new();
    readonly Dictionary<string, string> lastByAuthor = new();

    /// null = accept; otherwise the reason it was dropped.
    public string? RejectReason(Comment c)
    {
        if (c.AuthorId == pageId) return "comment của Page";

        var normalized = string.Join(' ', c.Message.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var isRepeat = false;
        if (c.AuthorId != "")
            lock (gate)
            {
                isRepeat = lastByAuthor.TryGetValue(c.AuthorId, out var last) && last == normalized;
                lastByAuthor[c.AuthorId] = normalized;
            }

        if (c.Message.Count(char.IsLetterOrDigit) < 3) return "quá ngắn";
        return isRepeat ? "trùng comment trước" : null;
    }
}
