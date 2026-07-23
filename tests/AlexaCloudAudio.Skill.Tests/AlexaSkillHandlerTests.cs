using AlexaCloudAudio.Application;
using AlexaCloudAudio.Domain;
using Xunit;

namespace AlexaCloudAudio.Skill.Tests;

public sealed class AlexaSkillHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Launch_explains_primary_command()
    {
        var response = await CreateHandler().HandleAsync(Request("LaunchRequest"));

        Assert.Contains("play", response.Response.OutputSpeech, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Response.ShouldEndSession);
    }

    [Fact]
    public async Task Play_sound_returns_audio_player_directive()
    {
        var item = Item("gq", "General Quarters");
        var handler = CreateHandler(AudioResolution.Found(item));

        var response = await handler.HandleAsync(Intent("PlaySoundIntent", "general quarters"));

        var directive = Assert.Single(response.Response.Directives);
        Assert.Equal("AudioPlayer.Play", directive.Type);
        Assert.Equal("https://audio.example/gq", directive.AudioItem!.Url);
        Assert.Equal("gq", directive.AudioItem.Token);
    }

    [Fact]
    public async Task Missing_sound_name_asks_for_it()
    {
        var response = await CreateHandler().HandleAsync(Intent("PlaySoundIntent", null));

        Assert.Contains("Which sound", response.Response.OutputSpeech, StringComparison.Ordinal);
        Assert.False(response.Response.ShouldEndSession);
    }

    [Fact]
    public async Task Not_found_repeats_requested_name_without_internal_details()
    {
        var response = await CreateHandler(AudioResolution.NotFound())
            .HandleAsync(Intent("PlaySoundIntent", "red alert"));

        Assert.Contains("red alert", response.Response.OutputSpeech, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", response.Response.OutputSpeech, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ambiguous_match_lists_three_deterministic_choices()
    {
        var candidates = new[]
        {
            Item("3", "Zulu Alert"), Item("1", "Alpha Alert"), Item("2", "Bravo Alert"), Item("4", "Charlie Alert")
        };
        var response = await CreateHandler(AudioResolution.Ambiguous(candidates))
            .HandleAsync(Intent("PlaySoundIntent", "alert"));

        Assert.Contains("Alpha Alert, or Bravo Alert, or Charlie Alert", response.Response.OutputSpeech, StringComparison.Ordinal);
        Assert.DoesNotContain("Zulu Alert", response.Response.OutputSpeech, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AMAZON.StopIntent")]
    [InlineData("AMAZON.CancelIntent")]
    [InlineData("AMAZON.PauseIntent")]
    public async Task Stop_cancel_and_pause_return_stop_directive(string intentName)
    {
        var response = await CreateHandler().HandleAsync(Intent(intentName));

        Assert.Equal("AudioPlayer.Stop", Assert.Single(response.Response.Directives).Type);
    }

    [Fact]
    public async Task Help_and_fallback_keep_session_open()
    {
        var handler = CreateHandler();
        var help = await handler.HandleAsync(Intent("AMAZON.HelpIntent"));
        var fallback = await handler.HandleAsync(Intent("AMAZON.FallbackIntent"));

        Assert.False(help.Response.ShouldEndSession);
        Assert.False(fallback.Response.ShouldEndSession);
        Assert.NotNull(help.Response.Reprompt);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 151)]
    public async Task Invalid_signature_and_stale_requests_are_rejected(bool signatureValid, int ageSeconds)
    {
        var request = Request("LaunchRequest") with
        {
            SignatureValid = signatureValid,
            Timestamp = Now.AddSeconds(-ageSeconds)
        };

        var response = await CreateHandler().HandleAsync(request);

        Assert.Contains("safely", response.Response.OutputSpeech, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Response.ShouldEndSession);
    }

    [Fact]
    public async Task Unsupported_request_is_rejected_safely()
    {
        var response = await CreateHandler().HandleAsync(Request("Connections.Response"));

        Assert.Contains("do not support", response.Response.OutputSpeech, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_processing()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateHandler().HandleAsync(Request("LaunchRequest"), source.Token));
    }

    private static AlexaSkillHandler CreateHandler(AudioResolution? resolution = null) =>
        new(
            new StubResolver(resolution ?? AudioResolution.NotFound()),
            new StubPlaybackUrls(),
            timeProvider: new FixedTimeProvider(Now));

    private static AlexaRequestEnvelope Request(string type) =>
        new("1.0", new AlexaRequest(type), Now);

    private static AlexaRequestEnvelope Intent(string name, string? soundName = null) =>
        new("1.0", new AlexaRequest("IntentRequest", name,
            soundName is null ? null : new Dictionary<string, string?> { ["soundName"] = soundName }), Now);

    private static AudioItem Item(string id, string name) =>
        new(id, name, $"{name}.wav", "audio/wav", 100, Now, "v1");

    private sealed class StubResolver(AudioResolution resolution) : IAudioResolver
    {
        public Task<AudioResolution> ResolveAsync(string requestedName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(resolution);
        }
    }

    private sealed class StubPlaybackUrls : IAudioPlaybackUrlProvider
    {
        public Task<Uri> GetPlaybackUriAsync(string providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Uri($"https://audio.example/{providerId}"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
