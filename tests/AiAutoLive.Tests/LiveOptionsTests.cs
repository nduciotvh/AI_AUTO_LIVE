using AiAutoLive;

public class LiveOptionsTests
{
    [Fact]
    public void MissingKeys_lists_empty_required_keys_in_order()
    {
        var o = new LiveOptions { PageId = "p", OpenAIApiKey = "k", OpenAIModel = " " };
        Assert.Equal(
            new[] { "Live:PageAccessToken", "Live:OpenAIModel", "Live:ElevenLabsApiKey", "Live:ElevenLabsVoiceId" },
            o.MissingKeys());
    }
}
