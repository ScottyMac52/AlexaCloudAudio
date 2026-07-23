namespace AlexaCloudAudio.Skill;

public sealed record AlexaRequestEnvelope(
    string Version,
    AlexaRequest Request,
    DateTimeOffset Timestamp,
    bool SignatureValid = true);

public sealed record AlexaRequest(
    string Type,
    string? IntentName = null,
    IReadOnlyDictionary<string, string?>? Slots = null,
    string? Token = null);

public sealed record AlexaResponseEnvelope(string Version, AlexaResponse Response);

public sealed record AlexaResponse(
    string? OutputSpeech,
    string? Reprompt,
    bool ShouldEndSession,
    IReadOnlyList<AlexaDirective> Directives);

public sealed record AlexaDirective(
    string Type,
    string? PlayBehavior = null,
    AlexaAudioStream? AudioItem = null);

public sealed record AlexaAudioStream(string Url, string Token, long OffsetInMilliseconds = 0);

public interface IAlexaRequestValidator
{
    AlexaValidationResult Validate(AlexaRequestEnvelope envelope, DateTimeOffset now);
}

public sealed record AlexaValidationResult(bool IsValid, string? Reason)
{
    public static AlexaValidationResult Valid() => new(true, null);
    public static AlexaValidationResult Invalid(string reason) => new(false, reason);
}

public interface IAudioPlaybackUrlProvider
{
    Task<Uri> GetPlaybackUriAsync(string providerId, CancellationToken cancellationToken = default);
}
