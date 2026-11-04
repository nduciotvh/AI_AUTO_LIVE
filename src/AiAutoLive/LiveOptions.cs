namespace AiAutoLive;

/// The "Live" config section. Secrets (*ApiKey, PageAccessToken) come from user-secrets or env vars only.
public sealed class LiveOptions
{
    public string PageId { get; set; } = "";
    public string LiveVideoId { get; set; } = "";
    public string GraphApiVersion { get; set; } = "v24.0";
    public int PollIntervalSeconds { get; set; } = 2;
    public int MaxPendingComments { get; set; } = 5;
    public int CommentMaxAgeSeconds { get; set; } = 60;
    public int ReplyMaxAgeSeconds { get; set; } = 90;
    public int MaxRepliesInRow { get; set; } = 2;
    public string OpenAIModel { get; set; } = "gpt-4.1-mini";
    public string OpenAIReasoningEffort { get; set; } = "";
    public string ElevenLabsVoiceId { get; set; } = "";
    public string ElevenLabsModelId { get; set; } = "eleven_flash_v2_5";

    public string PageAccessToken { get; set; } = "";
    public string OpenAIApiKey { get; set; } = "";
    public string ElevenLabsApiKey { get; set; } = "";

    public IReadOnlyList<string> MissingKeys() =>
        new (string Name, string Value)[]
        {
            (nameof(PageId), PageId),
            (nameof(PageAccessToken), PageAccessToken),
            (nameof(OpenAIApiKey), OpenAIApiKey),
            (nameof(OpenAIModel), OpenAIModel),
            (nameof(ElevenLabsApiKey), ElevenLabsApiKey),
            (nameof(ElevenLabsVoiceId), ElevenLabsVoiceId),
            (nameof(ElevenLabsModelId), ElevenLabsModelId),
        }
        .Where(k => string.IsNullOrWhiteSpace(k.Value))
        .Select(k => "Live:" + k.Name)
        .ToList();
}
