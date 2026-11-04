using System.Threading.Channels;
using AiAutoLive;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var section = builder.Configuration.GetSection("Live");
builder.Services.Configure<LiveOptions>(section);
var o = section.Get<LiveOptions>() ?? new();

// Fixed application name: moving the project folder must not make the saved keys unreadable.
builder.Services.AddDataProtection().SetApplicationName("AiAutoLive");
var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiAutoLive", "settings.dat");
builder.Services.AddSingleton(sp =>
    new SettingsStore(settingsPath, sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("AiAutoLive.Settings")));
builder.Services.AddOptions<LiveOptions>().PostConfigure<SettingsStore>((live, store) => store.ApplyTo(live));

var audioDir = Path.Combine(builder.Environment.ContentRootPath, "audio");
var playlistDir = Path.Combine(audioDir, "playlist");
Directory.CreateDirectory(playlistDir);
Directory.CreateDirectory(Path.Combine(audioDir, "replies"));
var playlist = PlaybackQueue.PlaylistUrls(playlistDir);

builder.Services.AddSingleton(new PlaybackQueue(playlist, o.MaxRepliesInRow, TimeSpan.FromSeconds(o.ReplyMaxAgeSeconds)));
builder.Services.AddSingleton(sp =>
    new CommentFilter(() => sp.GetRequiredService<IOptionsMonitor<LiveOptions>>().CurrentValue.PageId));
builder.Services.AddSingleton(Channel.CreateBounded<Comment>(
    new BoundedChannelOptions(o.MaxPendingComments) { FullMode = BoundedChannelFullMode.DropOldest }));
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
builder.Services.AddHostedService<ReplyWorker>();
builder.Services.AddHostedService<CommentPoller>();

var app = builder.Build();
app.Logger.LogInformation("Đã nạp {Count} clip playlist từ {Dir}", playlist.Count, playlistDir);
if (app.Services.GetRequiredService<IOptionsMonitor<LiveOptions>>().CurrentValue.MissingKeys() is { Count: > 0 } missing)
    app.Logger.LogWarning("Chưa có: {Keys}. Điền tại http://localhost:5000/settings.html", string.Join(", ", missing));

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

// Secrets go out masked; the page never receives a full key.
app.MapGet("/api/settings", (IOptionsMonitor<LiveOptions> options, IHostEnvironment env) =>
{
    var live = options.CurrentValue;
    var productPath = Path.Combine(env.ContentRootPath, ReplyWorker.ProductFile);
    var product = File.Exists(productPath) ? File.ReadAllText(productPath) : "";
    return Results.Ok(new Dictionary<string, object>
    {
        [nameof(live.OpenAIApiKey)] = SettingsStore.Mask(live.OpenAIApiKey),
        [nameof(live.ElevenLabsApiKey)] = SettingsStore.Mask(live.ElevenLabsApiKey),
        [nameof(live.PageAccessToken)] = SettingsStore.Mask(live.PageAccessToken),
        [nameof(live.PageId)] = live.PageId,
        [nameof(live.ElevenLabsVoiceId)] = live.ElevenLabsVoiceId,
        ["prompt"] = ReplyWorker.ReadPrompt(env.ContentRootPath),
        ["product"] = product,
        ["productUnedited"] = ReplyWorker.IsUnedited(product),
    });
});

app.MapPost("/api/settings", (SettingsUpdate body, SettingsStore store, IOptionsMonitorCache<LiveOptions> cache,
    IHostEnvironment env, ILogger<Program> log) =>
{
    if (body.Prompt?.Length > 20_000) return Results.BadRequest(new { error = "Prompt dài quá 20.000 ký tự" });
    if (body.Product is { } p && (string.IsNullOrWhiteSpace(p) || p.Length > 50_000))
        return Results.BadRequest(new { error = "Tài liệu sản phẩm không được để trống và tối đa 50.000 ký tự" });
    if (body.Values is { } values && store.Save(values) is { } error) return Results.BadRequest(new { error });

    if (body.Prompt is { } prompt)
    {
        var promptPath = Path.Combine(env.ContentRootPath, ReplyWorker.PromptFile);
        if (string.IsNullOrWhiteSpace(prompt)) File.Delete(promptPath); // blank = back to the built-in default prompt
        else File.WriteAllText(promptPath, prompt);
    }
    if (body.Product is { } product) File.WriteAllText(Path.Combine(env.ContentRootPath, ReplyWorker.ProductFile), product);

    cache.TryRemove(Options.DefaultName); // next CurrentValue re-applies the saved keys, no restart needed
    log.LogInformation("Đã lưu cài đặt");
    return Results.Ok();
});

app.Run();

record ManualComment(string? Name, string? Message);
record SettingsUpdate(Dictionary<string, string?>? Values, string? Prompt, string? Product);
