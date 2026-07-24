using AlexaCloudAudio.Application;
using AlexaCloudAudio.Domain;

namespace AlexaCloudAudio.Infrastructure;

public sealed record OneDriveOptions(
    string RootFolder,
    bool IncludeNestedFolders = true,
    int MaximumRetryAttempts = 3,
    TimeSpan? DefaultRetryDelay = null)
{
    public TimeSpan RetryDelay => DefaultRetryDelay ?? TimeSpan.FromMilliseconds(250);
}

public sealed record GraphDriveItem(
    string Id,
    string Name,
    bool IsFolder,
    string? MimeType,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    string? ETag,
    string? CTag);

public sealed record GraphDrivePage(
    IReadOnlyList<GraphDriveItem> Items,
    string? NextLink);

public sealed class GraphRequestException : Exception
{
    public GraphRequestException(string message, int statusCode, TimeSpan? retryAfter = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public int StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
    public bool IsTransient => StatusCode is 408 or 429 || StatusCode >= 500;
}

public interface IOneDriveGraphClient
{
    Task<GraphDriveItem> ResolveFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<GraphDrivePage> ListChildrenAsync(string folderId, string? nextLink, CancellationToken cancellationToken = default);
    Task<Uri> GetDownloadUriAsync(string itemId, CancellationToken cancellationToken = default);
}

public interface IOneDriveDownloadUrlProvider
{
    Task<Uri> GetDownloadUriAsync(string providerId, CancellationToken cancellationToken = default);
}

public sealed class OneDriveProvider : IAudioLibraryProvider, IOneDriveDownloadUrlProvider
{
    private readonly IOneDriveGraphClient graph;
    private readonly OneDriveOptions options;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public OneDriveProvider(
        IOneDriveGraphClient graph,
        OneDriveOptions options,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootFolder);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaximumRetryAttempts);
        this.delay = delay ?? Task.Delay;
    }

    public async Task<IReadOnlyList<AudioItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = await ExecuteAsync(
            token => graph.ResolveFolderAsync(options.RootFolder, token),
            cancellationToken).ConfigureAwait(false);

        if (!root.IsFolder)
        {
            throw new InvalidOperationException("The configured OneDrive root is not a folder.");
        }

        var results = new List<AudioItem>();
        var folders = new Queue<string>();
        folders.Enqueue(root.Id);

        while (folders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folderId = folders.Dequeue();
            string? nextLink = null;

            do
            {
                var page = await ExecuteAsync(
                    token => graph.ListChildrenAsync(folderId, nextLink, token),
                    cancellationToken).ConfigureAwait(false);

                foreach (var item in page.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.IsFolder)
                    {
                        if (options.IncludeNestedFolders)
                        {
                            folders.Enqueue(item.Id);
                        }

                        continue;
                    }

                    if (!AudioNameNormalizer.IsSupportedFileName(item.Name))
                    {
                        continue;
                    }

                    results.Add(Map(item));
                }

                nextLink = page.NextLink;
            }
            while (!string.IsNullOrWhiteSpace(nextLink));
        }

        return results
            .OrderBy(static item => item.ProviderId, StringComparer.Ordinal)
            .ToArray();
    }

    public Task<Uri> GetDownloadUriAsync(string providerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return ExecuteAsync(token => graph.GetDownloadUriAsync(providerId, token), cancellationToken);
    }

    private static AudioItem Map(GraphDriveItem item)
    {
        var version = !string.IsNullOrWhiteSpace(item.CTag)
            ? item.CTag
            : !string.IsNullOrWhiteSpace(item.ETag)
                ? item.ETag
                : $"{item.ModifiedAt.UtcTicks}:{item.SizeBytes}";

        return new AudioItem(
            item.Id,
            Path.GetFileNameWithoutExtension(item.Name),
            item.Name,
            item.MimeType ?? "application/octet-stream",
            item.SizeBytes,
            item.ModifiedAt,
            version);
    }

    private async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (GraphRequestException exception) when (
                exception.IsTransient && attempt < options.MaximumRetryAttempts)
            {
                var retryDelay = exception.RetryAfter ?? options.RetryDelay;
                await delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
