using AlexaCloudAudio.Domain;
using Xunit;

namespace AlexaCloudAudio.Application.Tests;

public sealed class AudioCatalogTests
{
    [Fact]
    public async Task Resolver_matches_filename_ignoring_extension_case_punctuation_and_whitespace()
    {
        var item = CreateItem("1", "General Quarters", "General---   Quarters.wav");
        var resolver = await CreateResolverAsync(item);

        var result = await resolver.ResolveAsync("GENERAL quarters.mp3");

        Assert.Equal(AudioResolutionStatus.Found, result.Status);
        Assert.Same(item, result.Item);
    }

    [Fact]
    public async Task Resolver_prefers_exact_name_over_alias()
    {
        var exact = CreateItem("1", "Alarm", "Alarm.wav");
        var alias = CreateItem("2", "Klaxon", "Klaxon.wav", ["alarm"]);
        var resolver = await CreateResolverAsync(alias, exact);

        var result = await resolver.ResolveAsync("alarm");

        Assert.Equal(AudioResolutionStatus.Found, result.Status);
        Assert.Same(exact, result.Item);
    }

    [Fact]
    public async Task Duplicate_normalized_names_return_deterministic_ambiguity()
    {
        var second = CreateItem("b", "Second", "General.Quarters.mp3");
        var first = CreateItem("a", "First", "General Quarters.wav");
        var resolver = await CreateResolverAsync(second, first);

        var result = await resolver.ResolveAsync("general quarters");

        Assert.Equal(AudioResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.Item);
        Assert.Equal(new[] { "a", "b" }, result.Candidates.Select(static item => item.ProviderId));
    }

    [Fact]
    public async Task Alias_resolves_when_no_exact_name_matches()
    {
        var item = CreateItem("1", "General Quarters", "GQ.wav", ["battle stations"]);
        var resolver = await CreateResolverAsync(item);

        var result = await resolver.ResolveAsync("Battle-Stations");

        Assert.Equal(AudioResolutionStatus.Found, result.Status);
        Assert.Same(item, result.Item);
    }

    [Fact]
    public async Task Catalog_rejects_alias_collisions_between_items()
    {
        var provider = new FakeProvider([
            CreateItem("1", "One", "One.wav", ["alert"]),
            CreateItem("2", "Two", "Two.wav", ["ALERT"])
        ]);
        var catalog = new AudioCatalog(provider);

        await Assert.ThrowsAsync<AudioCatalogValidationException>(() => catalog.RefreshAsync());
    }

    [Fact]
    public async Task Catalog_filters_unsupported_extensions()
    {
        var supported = CreateItem("1", "Supported", "Supported.wav");
        var provider = new FakeProvider([supported, CreateItem("2", "Unsupported", "Unsupported.txt")]);
        var catalog = new AudioCatalog(provider);

        await catalog.RefreshAsync();
        var items = await catalog.GetItemsAsync();

        Assert.Equal(new[] { supported }, items);
    }

    [Fact]
    public async Task Resolver_returns_not_found_for_unknown_name()
    {
        var resolver = await CreateResolverAsync(CreateItem("1", "Known", "Known.wav"));

        var result = await resolver.ResolveAsync("unknown");

        Assert.Equal(AudioResolutionStatus.NotFound, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Catalog_and_resolver_honor_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var catalog = new AudioCatalog(new FakeProvider([]));
        var resolver = new AudioResolver(catalog);

        await Assert.ThrowsAsync<OperationCanceledException>(() => catalog.RefreshAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => catalog.GetItemsAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => resolver.ResolveAsync("anything", cancellation.Token));
    }

    [Fact]
    public void Audio_item_preserves_provider_neutral_metadata_and_normalizes_aliases()
    {
        var modified = DateTimeOffset.Parse("2026-07-23T12:00:00Z");
        var item = new AudioItem("provider-id", "Display", "source.wav", "audio/wav", 123, modified, "etag-1", [" Alert ", "alert", ""]);

        Assert.Equal("provider-id", item.ProviderId);
        Assert.Equal("Display", item.DisplayName);
        Assert.Equal("source.wav", item.SourceFileName);
        Assert.Equal("audio/wav", item.MimeType);
        Assert.Equal(123, item.SizeBytes);
        Assert.Equal(modified, item.ModifiedAt);
        Assert.Equal("etag-1", item.Version);
        Assert.Equal(new[] { "Alert" }, item.Aliases);
    }

    private static async Task<AudioResolver> CreateResolverAsync(params AudioItem[] items)
    {
        var catalog = new AudioCatalog(new FakeProvider(items));
        await catalog.RefreshAsync();
        return new AudioResolver(catalog);
    }

    private static AudioItem CreateItem(string id, string displayName, string fileName, IEnumerable<string>? aliases = null) =>
        new(id, displayName, fileName, "audio/wav", 1, DateTimeOffset.UnixEpoch, "v1", aliases);

    private sealed class FakeProvider(IReadOnlyList<AudioItem> items) : IAudioLibraryProvider
    {
        public Task<IReadOnlyList<AudioItem>> ListAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(items);
        }
    }
}
