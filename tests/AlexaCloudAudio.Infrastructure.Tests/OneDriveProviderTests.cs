using Xunit;

namespace AlexaCloudAudio.Infrastructure.Tests;

public sealed class OneDriveProviderTests
{
    [Fact]
    public async Task Enumerates_supported_audio_from_configured_folder_and_nested_folders()
    {
        var graph = new FakeGraphClient
        {
            Root = Folder("root", "Audio"),
            Pages =
            {
                ["root"] = [new GraphDrivePage([File("2", "Alert.txt"), File("1", "General Quarters.wav"), Folder("nested", "Navy")], null)],
                ["nested"] = [new GraphDrivePage([File("3", "Bosun.mp3")], null)]
            }
        };
        var provider = new OneDriveProvider(graph, new OneDriveOptions("/Alexa Audio"));

        var items = await provider.ListAsync();

        Assert.Equal(new[] { "1", "3" }, items.Select(static item => item.ProviderId));
        Assert.Equal("General Quarters", items[0].DisplayName);
        Assert.Equal("/Alexa Audio", graph.ResolvedPath);
    }

    [Fact]
    public async Task Nested_folder_traversal_can_be_disabled()
    {
        var graph = new FakeGraphClient
        {
            Root = Folder("root", "Audio"),
            Pages = { ["root"] = [new GraphDrivePage([File("1", "Top.wav"), Folder("nested", "Nested")], null)] }
        };
        var provider = new OneDriveProvider(graph, new OneDriveOptions("Audio", IncludeNestedFolders: false));

        var items = await provider.ListAsync();

        Assert.Single(items);
        Assert.DoesNotContain("nested", graph.ListedFolders);
    }

    [Fact]
    public async Task Follows_all_graph_pages()
    {
        var graph = new FakeGraphClient
        {
            Root = Folder("root", "Audio"),
            Pages =
            {
                ["root"] =
                [
                    new GraphDrivePage([File("1", "One.wav")], "page-2"),
                    new GraphDrivePage([File("2", "Two.mp3")], null)
                ]
            }
        };
        var provider = new OneDriveProvider(graph, new OneDriveOptions("Audio"));

        var items = await provider.ListAsync();

        Assert.Equal(2, items.Count);
        Assert.Equal(new string?[] { null, "page-2" }, graph.NextLinks);
    }

    [Fact]
    public async Task Retries_throttling_with_server_delay_and_stops_at_bound()
    {
        var delays = new List<TimeSpan>();
        var graph = new FakeGraphClient
        {
            Root = Folder("root", "Audio"),
            FailuresRemaining = 2,
            Pages = { ["root"] = [new GraphDrivePage([File("1", "One.wav")], null)] }
        };
        var provider = new OneDriveProvider(
            graph,
            new OneDriveOptions("Audio", MaximumRetryAttempts: 2),
            (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        var items = await provider.ListAsync();

        Assert.Single(items);
        Assert.Equal(3, graph.ListAttempts);
        Assert.All(delays, delay => Assert.Equal(TimeSpan.FromSeconds(2), delay));
    }

    [Fact]
    public async Task Download_urls_are_reacquired_for_every_request()
    {
        var graph = new FakeGraphClient { Root = Folder("root", "Audio") };
        var provider = new OneDriveProvider(graph, new OneDriveOptions("Audio"));

        var first = await provider.GetDownloadUriAsync("item");
        var second = await provider.GetDownloadUriAsync("item");

        Assert.NotEqual(first, second);
        Assert.Equal(2, graph.DownloadRequests);
    }

    [Fact]
    public async Task Refresh_results_reflect_deleted_and_renamed_files()
    {
        var graph = new FakeGraphClient
        {
            Root = Folder("root", "Audio"),
            Pages = { ["root"] = [new GraphDrivePage([File("1", "Old.wav")], null)] }
        };
        var provider = new OneDriveProvider(graph, new OneDriveOptions("Audio"));

        var first = await provider.ListAsync();
        graph.Pages["root"] = [new GraphDrivePage([File("1", "Renamed.wav"), File("2", "New.mp3")], null)];
        graph.ResetPaging();
        var second = await provider.ListAsync();

        Assert.Equal("Old.wav", Assert.Single(first).SourceFileName);
        Assert.Equal(new[] { "Renamed.wav", "New.mp3" }, second.Select(static item => item.SourceFileName));
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_graph_boundary()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new OneDriveProvider(new FakeGraphClient { Root = Folder("root", "Audio") }, new OneDriveOptions("Audio"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.ListAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetDownloadUriAsync("item", cancellation.Token));
    }

    private static GraphDriveItem Folder(string id, string name) =>
        new(id, name, true, null, 0, DateTimeOffset.UnixEpoch, null, null);

    private static GraphDriveItem File(string id, string name) =>
        new(id, name, false, "audio/test", 42, DateTimeOffset.UnixEpoch, $"etag-{id}", $"ctag-{id}");

    private sealed class FakeGraphClient : IOneDriveGraphClient
    {
        private readonly Dictionary<string, int> pageIndexes = new(StringComparer.Ordinal);
        public GraphDriveItem Root { get; init; } = Folder("root", "Audio");
        public Dictionary<string, IReadOnlyList<GraphDrivePage>> Pages { get; } = new(StringComparer.Ordinal);
        public List<string> ListedFolders { get; } = [];
        public List<string?> NextLinks { get; } = [];
        public string? ResolvedPath { get; private set; }
        public int FailuresRemaining { get; set; }
        public int ListAttempts { get; private set; }
        public int DownloadRequests { get; private set; }

        public Task<GraphDriveItem> ResolveFolderAsync(string folderPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolvedPath = folderPath;
            return Task.FromResult(Root);
        }

        public Task<GraphDrivePage> ListChildrenAsync(string folderId, string? nextLink, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListAttempts++;
            if (FailuresRemaining-- > 0)
            {
                throw new GraphRequestException("throttled", 429, TimeSpan.FromSeconds(2));
            }

            ListedFolders.Add(folderId);
            NextLinks.Add(nextLink);
            var index = pageIndexes.GetValueOrDefault(folderId);
            pageIndexes[folderId] = index + 1;
            return Task.FromResult(Pages.GetValueOrDefault(folderId, [new GraphDrivePage([], null)])[index]);
        }

        public Task<Uri> GetDownloadUriAsync(string itemId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DownloadRequests++;
            return Task.FromResult(new Uri($"https://download.example/{itemId}?request={DownloadRequests}"));
        }

        public void ResetPaging() => pageIndexes.Clear();
    }
}
