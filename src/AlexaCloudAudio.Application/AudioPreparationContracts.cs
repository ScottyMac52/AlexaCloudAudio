using AlexaCloudAudio.Domain;

namespace AlexaCloudAudio.Application;

public sealed record PreparedAudio(
    string CacheKey,
    string ContentType,
    long Length,
    Func<CancellationToken, Task<Stream>> OpenReadAsync);

public sealed record PlaybackGrant(string PreparedAudioKey, DateTimeOffset ExpiresAt);

public sealed record AudioRange(long Start, long End)
{
    public long Length => checked(End - Start + 1);
}

public sealed record AudioStreamResult(
    int StatusCode,
    string ContentType,
    long ContentLength,
    AudioRange? Range,
    Stream Content);

public interface IAudioSourceContentProvider
{
    Task<Stream> OpenReadAsync(string providerId, CancellationToken cancellationToken = default);
}

public interface IPreparedAudioCache
{
    Task<PreparedAudio?> GetAsync(string cacheKey, CancellationToken cancellationToken = default);
    Task PutAsync(PreparedAudio audio, CancellationToken cancellationToken = default);
    Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);
}

public interface IAudioTranscoder
{
    Task<PreparedAudio> TranscodeToMp3Async(
        AudioItem item,
        string cacheKey,
        Stream source,
        CancellationToken cancellationToken = default);
}

public interface IAudioPreparationService
{
    Task<PreparedAudio> PrepareAsync(AudioItem item, CancellationToken cancellationToken = default);
}

public interface IPlaybackGrantService
{
    string Issue(PlaybackGrant grant);
    bool TryValidate(string token, DateTimeOffset now, out PlaybackGrant? grant);
}

public interface IAudioStreamingService
{
    Task<AudioStreamResult?> OpenAsync(
        string token,
        DateTimeOffset now,
        string? rangeHeader,
        CancellationToken cancellationToken = default);
}
