using AlexaCloudAudio.Domain;

namespace AlexaCloudAudio.Application;

public sealed class AudioCatalog : IAudioCatalog
{
    private readonly IAudioLibraryProvider provider;
    private IReadOnlyList<AudioItem> items = [];

    public AudioCatalog(IAudioLibraryProvider provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var discovered = await provider.ListAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var supported = discovered
            .Where(static item => AudioNameNormalizer.IsSupportedFileName(item.SourceFileName))
            .OrderBy(static item => item.ProviderId, StringComparer.Ordinal)
            .ToArray();

        ValidateAliasCollisions(supported);
        items = supported;
    }

    public Task<IReadOnlyList<AudioItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items);
    }

    private static void ValidateAliasCollisions(IReadOnlyList<AudioItem> audioItems)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var item in audioItems)
        {
            foreach (var alias in item.Aliases)
            {
                var normalized = AudioNameNormalizer.Normalize(alias);
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (owners.TryGetValue(normalized, out var owner) && !StringComparer.Ordinal.Equals(owner, item.ProviderId))
                {
                    throw new AudioCatalogValidationException($"Alias '{alias}' is assigned to multiple audio items.");
                }

                owners[normalized] = item.ProviderId;
            }
        }
    }
}
