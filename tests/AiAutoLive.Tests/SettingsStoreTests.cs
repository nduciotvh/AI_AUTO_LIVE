using AiAutoLive;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public class SettingsStoreTests
{
    readonly IDataProtector protector = new EphemeralDataProtectionProvider().CreateProtector("test");
    readonly string path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "settings.dat");

    SettingsStore Store() => new(path, protector);

    static LiveOptions Applied(SettingsStore s)
    {
        var o = new LiveOptions();
        s.ApplyTo(o);
        return o;
    }

    [Fact]
    public void Saved_values_survive_reload_and_are_encrypted_on_disk()
    {
        Assert.Null(Store().Save(new Dictionary<string, string?> { ["OpenAIApiKey"] = "sk-test-123456", ["PageId"] = "1234567890" }));
        var o = Applied(Store());
        Assert.Equal("sk-test-123456", o.OpenAIApiKey);
        Assert.Equal("1234567890", o.PageId);
        Assert.DoesNotContain("sk-test-123456", File.ReadAllText(path));
    }

    [Fact]
    public void Blank_value_keeps_saved_one()
    {
        var s = Store();
        s.Save(new Dictionary<string, string?> { ["PageAccessToken"] = "EAAB-token" });
        s.Save(new Dictionary<string, string?> { ["PageAccessToken"] = "  ", ["ElevenLabsApiKey"] = null });
        Assert.Equal("EAAB-token", Applied(s).PageAccessToken);
    }

    [Fact]
    public void Values_are_trimmed()
    {
        var s = Store();
        s.Save(new Dictionary<string, string?> { ["ElevenLabsVoiceId"] = "  WAhoMTNdLdMoq1j3wf3I \n" });
        Assert.Equal("WAhoMTNdLdMoq1j3wf3I", Applied(s).ElevenLabsVoiceId);
    }

    [Fact]
    public void Unknown_name_is_rejected_and_nothing_is_saved()
    {
        var s = Store();
        Assert.NotNull(s.Save(new Dictionary<string, string?> { ["PageId"] = "1", ["OpenAIModel"] = "x" }));
        Assert.Equal("", Applied(s).PageId);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Too_long_value_is_rejected() =>
        Assert.NotNull(Store().Save(new Dictionary<string, string?> { ["OpenAIApiKey"] = new string('k', 2001) }));

    [Fact]
    public void Unreadable_file_starts_empty()
    {
        File.WriteAllText(path, "not encrypted");
        Assert.Equal("", Applied(Store()).OpenAIApiKey);
    }

    [Fact]
    public void Unsaved_fields_keep_config_values()
    {
        var o = new LiveOptions { OpenAIApiKey = "from-config" };
        Store().ApplyTo(o);
        Assert.Equal("from-config", o.OpenAIApiKey);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("short", "••••")]
    [InlineData("sk-abcdefgh1234", "…1234")]
    public void Mask_shows_only_last_four(string value, string expected) =>
        Assert.Equal(expected, SettingsStore.Mask(value));

    [Fact]
    public void Saved_key_reaches_options_monitor_without_restart()
    {
        var store = Store();
        var services = new ServiceCollection().AddSingleton(store);
        services.AddOptions<LiveOptions>().PostConfigure<SettingsStore>((o, s) => s.ApplyTo(o));
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<LiveOptions>>();
        Assert.Equal("", monitor.CurrentValue.OpenAIApiKey);

        store.Save(new Dictionary<string, string?> { ["OpenAIApiKey"] = "sk-new" });
        provider.GetRequiredService<IOptionsMonitorCache<LiveOptions>>().TryRemove(Options.DefaultName);

        Assert.Equal("sk-new", monitor.CurrentValue.OpenAIApiKey);
    }
}
