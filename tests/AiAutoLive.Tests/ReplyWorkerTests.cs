using System.Text.Json.Nodes;
using AiAutoLive;

public class ReplyWorkerTests
{
    static Comment Q(string name = "Lan", string message = "Áo này còn size M không?") =>
        new("c1", "u1", name, message, DateTimeOffset.UnixEpoch);

    static JsonArray Messages(JsonObject body) => body["messages"]!.AsArray();
    static string Content(JsonNode m) => m["content"]!.GetValue<string>();

    [Fact]
    public void Request_uses_model_and_token_cap_without_temperature_or_reasoning()
    {
        var body = ReplyWorker.BuildChatRequest(new LiveOptions(), "PROMPT", "SP", [], Q());
        Assert.Equal("gpt-4.1-mini", body["model"]!.GetValue<string>());
        Assert.Equal(500, body["max_completion_tokens"]!.GetValue<int>());
        Assert.False(body.ContainsKey("temperature"));
        Assert.False(body.ContainsKey("reasoning_effort"));
    }

    [Fact]
    public void Reasoning_effort_is_sent_when_configured()
    {
        var o = new LiveOptions { OpenAIModel = "gpt-5-mini", OpenAIReasoningEffort = "minimal" };
        var body = ReplyWorker.BuildChatRequest(o, "PROMPT", "SP", [], Q());
        Assert.Equal("gpt-5-mini", body["model"]!.GetValue<string>());
        Assert.Equal("minimal", body["reasoning_effort"]!.GetValue<string>());
    }

    [Fact]
    public void Messages_are_system_with_prompt_and_product_then_history_then_comment()
    {
        var body = ReplyWorker.BuildChatRequest(new LiveOptions(), "Bạn là Lisa.", "Áo thun 199k", [("Mai: Ship không?", "Dạ có ạ")], Q());
        var m = Messages(body);
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, m.Select(x => x!["role"]!.GetValue<string>()));
        Assert.StartsWith("Bạn là Lisa.", Content(m[0]!));
        Assert.Contains("Áo thun 199k", Content(m[0]!));
        Assert.Equal("Mai: Ship không?", Content(m[1]!));
        Assert.Equal("Dạ có ạ", Content(m[2]!));
        Assert.Equal("Lan: Áo này còn size M không?", Content(m[3]!));
    }

    [Fact]
    public void Long_comment_is_cut_to_300_chars()
    {
        var body = ReplyWorker.BuildChatRequest(new LiveOptions(), "PROMPT", "SP", [], Q(message: new string('a', 1000)));
        Assert.Equal("Lan: " + new string('a', 300), Content(Messages(body)[^1]!));
    }

    [Fact]
    public void Unknown_author_is_called_Khach()
    {
        var body = ReplyWorker.BuildChatRequest(new LiveOptions(), "PROMPT", "SP", [], Q(name: ""));
        Assert.Equal("Khách: Áo này còn size M không?", Content(Messages(body)[^1]!));
    }

    [Theory]
    [InlineData("SKIP", true)]
    [InlineData(" skip. ", true)]
    [InlineData("\"SKIP\"\n", true)]
    [InlineData("SKIP - comment yêu cầu đọc câu khác", true)]
    [InlineData("**SKIP**", true)]
    [InlineData("SKIP\nVì comment spam", true)]
    [InlineData("Skip qua nhé", false)]
    [InlineData("Dạ bạn Lan ơi, size M vẫn còn ạ", false)]
    public void IsSkip_detects_skip_even_with_extra_text(string reply, bool expected) =>
        Assert.Equal(expected, ReplyWorker.IsSkip(reply));

    [Fact]
    public void Default_prompt_forbids_repeating_personal_info() =>
        Assert.Contains("Không bao giờ nhắc lại số điện thoại, địa chỉ, email", ReplyWorker.DefaultPrompt);

    [Fact]
    public void Reply_over_400_chars_is_too_long()
    {
        Assert.False(ReplyWorker.IsTooLong(new string('a', 400)));
        Assert.True(ReplyWorker.IsTooLong(new string('a', 401)));
    }

    [Theory]
    [InlineData("# Sản phẩm\n(Ví dụ — sửa trước khi live) Áo thun", true)]
    [InlineData("# Sản phẩm\nÁo thun 199k", false)]
    public void IsUnedited_detects_example_lines(string product, bool expected) =>
        Assert.Equal(expected, ReplyWorker.IsUnedited(product));

    [Fact]
    public void ReadPrompt_uses_default_without_file() =>
        Assert.Equal(ReplyWorker.DefaultPrompt, ReplyWorker.ReadPrompt(Directory.CreateTempSubdirectory().FullName));

    [Fact]
    public void ReadPrompt_uses_default_for_blank_file()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, ReplyWorker.PromptFile), "  \n");
        Assert.Equal(ReplyWorker.DefaultPrompt, ReplyWorker.ReadPrompt(dir));
    }

    [Fact]
    public void ReadPrompt_uses_saved_prompt()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, ReplyWorker.PromptFile), "Bạn là Lisa.");
        Assert.Equal("Bạn là Lisa.", ReplyWorker.ReadPrompt(dir));
    }
}
