using AlexaCloudAudio.Domain;

namespace AlexaCloudAudio.Application;

public interface IAudioLibraryProvider
{
    Task<IReadOnlyList<AudioItem>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IAudioCatalog
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AudioItem>> GetItemsAsync(CancellationToken cancellationToken = default);
}

public interface IAudioResolver
{
    Task<AudioResolution> ResolveAsync(string requestedName, CancellationToken cancellationToken = default);
}

public enum AudioResolutionStatus
{
    Found,
    NotFound,
    Ambiguous
}

public sealed record AudioResolution(AudioResolutionStatus Status, AudioItem? Item, IReadOnlyList<AudioItem> Candidates)
{
    public static AudioResolution Found(AudioItem item) => new(AudioResolutionStatus.Found, item, [item]);
    public static AudioResolution NotFound() => new(AudioResolutionStatus.NotFound, null, []);
    public static AudioResolution Ambiguous(IReadOnlyList<AudioItem> candidates) => new(AudioResolutionStatus.Ambiguous, null, candidates);
}

public sealed class AudioCatalogValidationException : Exception
{
    public AudioCatalogValidationException(string message) : base(message)
    {
    }
}
