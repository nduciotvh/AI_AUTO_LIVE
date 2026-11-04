using System.Threading.Channels;
using AiAutoLive;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

var section = builder.Configuration.GetSection("Live");
builder.Services.Configure<LiveOptions>(section);
var o = section.Get<LiveOptions>() ?? new();
if (o.MissingKeys() is { Count: > 0 } missing)
    throw new InvalidOperationException("Thiếu cấu hình: " + string.Join(", ", missing));

var audioDir = Path.Combine(builder.Environment.ContentRootPath, "audio");
var playlistDir = Path.Combine(audioDir, "playlist");
Directory.CreateDirectory(playlistDir);
Directory.CreateDirectory(Path.Combine(audioDir, "replies"));
var playlist = PlaybackQueue.PlaylistUrls(playlistDir);

builder.Services.AddSingleton(new PlaybackQueue(playlist, o.MaxRepliesInRow, TimeSpan.FromSeconds(o.ReplyMaxAgeSeconds)));
builder.Services.AddSingleton(new CommentFilter(o.PageId));
builder.Services.AddSingleton(Channel.CreateBounded<Comment>(
    new BoundedChannelOptions(o.MaxPendingComments) { FullMode = BoundedChannelFullMode.DropOldest }));
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
builder.Services.AddHostedService<ReplyWorker>();

var app = builder.Build();
app.Logger.LogInformation("Đã nạp {Count} clip playlist từ {Dir}", playlist.Count, playlistDir);

app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(audioDir), RequestPath = "/audio" });

app.MapGet("/api/next", (PlaybackQueue queue, ILogger<Program> log) =>
{
    var item = queue.Next(DateTimeOffset.UtcNow);
    if (item is null) return Results.NoContent();
    log.LogInformation("Phát {Url}", item.Url);
    return Results.Ok(new { url = item.Url });
});

app.MapPost("/api/comments", (ManualComment body, CommentFilter filter, Channel<Comment> comments, ILogger<Program> log) =>
{
    if (string.IsNullOrWhiteSpace(body.Message)) return Results.BadRequest();
    var name = body.Name?.Trim() ?? "";
    var c = new Comment(Guid.NewGuid().ToString("N"), "manual:" + name, name, body.Message, DateTimeOffset.UtcNow);
    var reason = filter.RejectReason(c);
    if (reason is null) comments.Writer.TryWrite(c);
    else log.LogInformation("Loại comment ({Reason}): {Message}", reason, c.Message);
    return Results.Ok(new { accepted = reason is null, reason });
});

app.Run();

record ManualComment(string? Name, string? Message);
