using AlexaCloudAudio.Application;

namespace AlexaCloudAudio.Skill;

public sealed class AlexaSkillHandler
{
    private const string HelpText = "You can say, play, followed by the name of a sound, such as play general quarters.";
    private readonly IAudioResolver resolver;
    private readonly IAudioPlaybackUrlProvider playbackUrls;
    private readonly IAlexaRequestValidator validator;
    private readonly TimeProvider timeProvider;

    public AlexaSkillHandler(
        IAudioResolver resolver,
        IAudioPlaybackUrlProvider playbackUrls,
        IAlexaRequestValidator? validator = null,
        TimeProvider? timeProvider = null)
    {
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.playbackUrls = playbackUrls ?? throw new ArgumentNullException(nameof(playbackUrls));
        this.validator = validator ?? new AlexaRequestValidator();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AlexaResponseEnvelope> HandleAsync(
        AlexaRequestEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = validator.Validate(envelope, timeProvider.GetUtcNow());
        if (!validation.IsValid)
        {
            return Tell("I could not process that request safely.");
        }

        return envelope.Request.Type switch
        {
            "LaunchRequest" => Ask($"Welcome to Cloud Audio. {HelpText}", HelpText),
            "IntentRequest" => await HandleIntentAsync(envelope.Request, cancellationToken).ConfigureAwait(false),
            "AudioPlayer.PlaybackStarted" or "AudioPlayer.PlaybackFinished" or "AudioPlayer.PlaybackStopped" => Silent(),
            _ => Tell("I do not support that request.")
        };
    }

    private async Task<AlexaResponseEnvelope> HandleIntentAsync(
        AlexaRequest request,
        CancellationToken cancellationToken)
    {
        return request.IntentName switch
        {
            "PlaySoundIntent" => await PlayAsync(request, cancellationToken).ConfigureAwait(false),
            "AMAZON.HelpIntent" => Ask(HelpText, HelpText),
            "AMAZON.StopIntent" or "AMAZON.CancelIntent" => Directive("AudioPlayer.Stop"),
            "AMAZON.PauseIntent" => Directive("AudioPlayer.Stop"),
            "AMAZON.ResumeIntent" => Tell("Resume is not available until a previous playback session has been established."),
            "AMAZON.FallbackIntent" => Ask($"I did not understand that. {HelpText}", HelpText),
            _ => Tell("I do not support that request.")
        };
    }

    private async Task<AlexaResponseEnvelope> PlayAsync(
        AlexaRequest request,
        CancellationToken cancellationToken)
    {
        var requestedName = request.Slots is not null && request.Slots.TryGetValue("soundName", out var slot)
            ? slot?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(requestedName))
        {
            return Ask("Which sound would you like me to play?", "Say play followed by a sound name.");
        }

        var resolution = await resolver.ResolveAsync(requestedName, cancellationToken).ConfigureAwait(false);
        if (resolution.Status == AudioResolutionStatus.NotFound)
        {
            return Ask($"I could not find {requestedName}. Try another sound name.", "Which sound would you like?");
        }

        if (resolution.Status == AudioResolutionStatus.Ambiguous)
        {
            var choices = resolution.Candidates
                .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .Select(static item => item.DisplayName)
                .ToArray();
            return Ask($"I found more than one match for {requestedName}. Try {string.Join(", or ", choices)}.", "Which one would you like?");
        }

        var item = resolution.Item!;
        var uri = await playbackUrls.GetPlaybackUriAsync(item.ProviderId, cancellationToken).ConfigureAwait(false);
        var directive = new AlexaDirective(
            "AudioPlayer.Play",
            "REPLACE_ALL",
            new AlexaAudioStream(uri.AbsoluteUri, item.ProviderId));
        return new AlexaResponseEnvelope("1.0", new AlexaResponse(null, null, true, [directive]));
    }

    private static AlexaResponseEnvelope Ask(string speech, string reprompt) =>
        new("1.0", new AlexaResponse(speech, reprompt, false, []));

    private static AlexaResponseEnvelope Tell(string speech) =>
        new("1.0", new AlexaResponse(speech, null, true, []));

    private static AlexaResponseEnvelope Directive(string type) =>
        new("1.0", new AlexaResponse(null, null, true, [new AlexaDirective(type)]));

    private static AlexaResponseEnvelope Silent() =>
        new("1.0", new AlexaResponse(null, null, true, []));
}
