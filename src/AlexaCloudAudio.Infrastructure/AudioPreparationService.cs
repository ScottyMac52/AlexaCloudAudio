using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AlexaCloudAudio.Application;
using AlexaCloudAudio.Domain;

namespace AlexaCloudAudio.Infrastructure;

public sealed record AudioPreparationOptions(
    long MaximumSourceBytes = 50 * 1024 * 1024,
    TimeSpan? ProcessingTimeout = null,
    int MaximumConcurrentPreparations = 2)
{
    public TimeSpan Timeout => ProcessingTimeout ?? TimeSpan.FromMinutes(2);
}

public sealed class AudioPreparationException : Exception
{
    public AudioPreparationException(string message) : base(message) { }
}

public sealed class AudioPreparationService : IAudioPreparationService
{
    private readonly IAudioSourceContentProvider sources;
    private readonly IPreparedAudioCache cache;
    private readonly IAudioTranscoder transcoder;
    private readonly AudioPreparationOptions options;
    private readonly SemaphoreSlim concurrency;
    private readonly ConcurrentDictionary<string, Lazy<Task<PreparedAudio>>> inFlight = new(StringComparer.Ordinal);

    public AudioPreparationService(
        IAudioSourceContentProvider sources,
        IPreparedAudioCache cache,
        IAudioTranscoder transcoder,
        AudioPreparationOptions? options = null)
    {
        this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.transcoder = transcoder ?? throw new ArgumentNullException(nameof(transcoder));
        this.options = options ?? new AudioPreparationOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.options.MaximumSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.options.MaximumConcurrentPreparations);
        concurrency = new SemaphoreSlim(this.options.MaximumConcurrentPreparations, this.options.MaximumConcurrentPreparations);
    }

    public async Task<PreparedAudio> PrepareAsync(AudioItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        if (item.SizeBytes > options.MaximumSourceBytes)
        {
            throw new AudioPreparationException("The source audio exceeds the configured size limit.");
        }

        var key = CreateCacheKey(item.ProviderId, item.Version);
        var cached = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var work = inFlight.GetOrAdd(key, _ => new Lazy<Task<PreparedAudio>>(
            () => PrepareUncachedAsync(item, key, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await work.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (work.IsValueCreated && work.Value.IsCompleted)
            {
                inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<PreparedAudio>>>(key, work));
            }
        }
    }

    public static string CreateCacheKey(string providerId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{providerId}\n{version}\nmp3-v1"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<PreparedAudio> PrepareUncachedAsync(AudioItem item, string key, CancellationToken callerToken)
    {
        await concurrency.WaitAsync(callerToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(options.Timeout);
        try
        {
            await using var source = await sources.OpenReadAsync(item.ProviderId, timeout.Token).ConfigureAwait(false);
            PreparedAudio prepared;
            if (IsCompatibleMp3(item))
            {
                prepared = await CopyCompatibleAsync(key, source, timeout.Token).ConfigureAwait(false);
            }
            else
            {
                prepared = await transcoder.TranscodeToMp3Async(item, key, source, timeout.Token).ConfigureAwait(false);
            }

            await cache.PutAsync(prepared, timeout.Token).ConfigureAwait(false);
            return prepared;
        }
        catch
        {
            await cache.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            concurrency.Release();
        }
    }

    private static bool IsCompatibleMp3(AudioItem item) =>
        string.Equals(Path.GetExtension(item.SourceFileName), ".mp3", StringComparison.OrdinalIgnoreCase)
        && string.Equals(item.MimeType, "audio/mpeg", StringComparison.OrdinalIgnoreCase);

    private async Task<PreparedAudio> CopyCompatibleAsync(string key, Stream source, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (buffer.Length > options.MaximumSourceBytes)
        {
            throw new AudioPreparationException("The source audio exceeds the configured size limit.");
        }

        var bytes = buffer.ToArray();
        return new PreparedAudio(key, "audio/mpeg", bytes.LongLength,
            _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
    }
}
