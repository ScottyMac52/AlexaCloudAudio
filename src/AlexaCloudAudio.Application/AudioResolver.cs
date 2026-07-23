using AlexaCloudAudio.Domain;

namespace AlexaCloudAudio.Application;

public sealed class AudioResolver : IAudioResolver
{
    private readonly IAudioCatalog catalog;

    public AudioResolver(IAudioCatalog catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<AudioResolution> ResolveAsync(string requestedName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedRequest = AudioNameNormalizer.Normalize(requestedName);
        var items = await catalog.GetItemsAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var nameMatches = items
            .Where(item => StringComparer.Ordinal.Equals(AudioNameNormalizer.Normalize(item.SourceFileName), normalizedRequest)
                || StringComparer.Ordinal.Equals(AudioNameNormalizer.Normalize(item.DisplayName), normalizedRequest))
            .OrderBy(static item => item.ProviderId, StringComparer.Ordinal)
            .ToArray();

        if (nameMatches.Length == 1)
        {
            return AudioResolution.Found(nameMatches[0]);
        }

        if (nameMatches.Length > 1)
        {
            return AudioResolution.Ambiguous(nameMatches);
        }

        var aliasMatches = items
            .Where(item => item.Aliases.Any(alias => StringComparer.Ordinal.Equals(AudioNameNormalizer.Normalize(alias), normalizedRequest)))
            .OrderBy(static item => item.ProviderId, StringComparer.Ordinal)
            .ToArray();

        return aliasMatches.Length switch
        {
            0 => AudioResolution.NotFound(),
            1 => AudioResolution.Found(aliasMatches[0]),
            _ => AudioResolution.Ambiguous(aliasMatches)
        };
    }
}
