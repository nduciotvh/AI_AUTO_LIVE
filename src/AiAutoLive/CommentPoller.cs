using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace AiAutoLive;

/// Polls the Graph API for new comments on the Page's live video and queues the ones worth answering.
public sealed class CommentPoller(HttpClient http, IOptions<LiveOptions> options, CommentFilter filter,
    Channel<Comment> comments, ILogger<CommentPoller> log) : BackgroundService
{
    record GraphList<T>(List<T>? Data);
    record GraphId(string Id);
    record GraphUser(string Id, string Name);
    record GraphComment(string Id, string? Message, GraphUser? From);

    /// Marks every id as seen. The first poll only seeds `seen`, so restarting mid-live never answers old comments.
    public static List<Comment> NewComments(HashSet<string> seen, IReadOnlyList<Comment> newestFirst, bool firstPoll)
    {
        var fresh = newestFirst.Reverse().Where(c => seen.Add(c.Id)).ToList();
        return firstPoll ? [] : fresh;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var videoId = o.LiveVideoId;
        while (videoId == "")
        {
            var live = await GetAsync<GraphList<GraphId>>($"{o.PageId}/live_videos?broadcast_status=%5B%22LIVE%22%5D&fields=id", stoppingToken);
            videoId = live is { Data: [var v, ..] } ? v.Id : "";
            if (videoId != "") break;
            log.LogInformation("Chưa thấy video LIVE trên Page {PageId}, thử lại sau 10 giây", o.PageId);
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        log.LogInformation("Đang đọc comment của video {VideoId}", videoId);

        var seen = new HashSet<string>();
        var first = true;
        while (true)
        {
            try
            {
                var page = await GetAsync<GraphList<GraphComment>>(
                    $"{videoId}/comments?filter=stream&order=reverse_chronological&fields=id,message,from{{id,name}}&limit=50", stoppingToken);
                if (page is not null)
                {
                    // shortcut: age counts from when we received the comment (≤ PollIntervalSeconds late), avoids clock skew vs created_time
                    var now = DateTimeOffset.UtcNow;
                    var batch = (page.Data ?? []).Select(g => new Comment(g.Id, g.From?.Id ?? "", g.From?.Name ?? "", g.Message ?? "", now)).ToList();
                    foreach (var c in NewComments(seen, batch, first)) Submit(c);
                    first = false;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(ex, "Lỗi khi đọc comment");
            }
            await Task.Delay(TimeSpan.FromSeconds(o.PollIntervalSeconds), stoppingToken);
        }
    }

    void Submit(Comment c)
    {
        if (filter.RejectReason(c) is { } reason)
        {
            log.LogInformation("Loại comment ({Reason}): {Message}", reason, c.Message);
            return;
        }
        log.LogInformation("Nhận comment {Name}: {Message}", c.AuthorName, c.Message);
        comments.Writer.TryWrite(c);
    }

    /// null on any failure (already logged); callers simply try again on their next round.
    async Task<T?> GetAsync<T>(string pathAndQuery, CancellationToken ct) where T : class
    {
        var o = options.Value;
        try
        {
            // Never log this URL: it carries the access token.
            var url = $"https://graph.facebook.com/{o.GraphApiVersion}/{pathAndQuery}&access_token={Uri.EscapeDataString(o.PageAccessToken)}";
            using var res = await http.GetAsync(url, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode) return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web);
            if (body.Contains("\"code\":190")) log.LogError("Page Access Token hết hạn");
            else log.LogWarning("Graph API lỗi {Status}: {Body}", (int)res.StatusCode, body);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Không gọi được Graph API: {Error}", ex.Message);
        }
        return null;
    }
}
