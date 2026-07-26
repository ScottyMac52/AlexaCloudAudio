using System.Text;
using AlexaCloudAudio.Application;
using AlexaCloudAudio.Domain;
using Xunit;

namespace AlexaCloudAudio.Infrastructure.Tests;

public sealed class SecureAudioStreamingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Compatible_mp3_is_cached_and_reused_without_transcoding()
    {
        var cache = new MemoryPreparedAudioCache();
        var source = new FakeSource("ready");
        var transcoder = new FakeTranscoder();
        var service = new AudioPreparationService(source, cache, transcoder);
        var item = Item("one", "v1", "ready.mp3", "audio/mpeg", 5);

        var first = await service.PrepareAsync(item);
        var second = await service.PrepareAsync(item);

        Assert.Same(first, second);
        Assert.Equal("audio/mpeg", first.ContentType);
        Assert.Equal(1, source.OpenCount);
        Assert.Equal(0, transcoder.CallCount);
    }

    [Fact]
    public async Task Wav_is_prepared_as_documented_mp3_format()
    {
        var transcoder = new FakeTranscoder("encoded");
        var service = new AudioPreparationService(
            new FakeSource("wave"),
            new MemoryPreparedAudioCache(),
            transcoder);

        var prepared = await service.PrepareAsync(Item("gq", "v1", "General Quarters.wav", "audio/wav", 4));

        Assert.Equal("audio/mpeg", prepared.ContentType);
        Assert.Equal("encoded".Length, prepared.Length);
        Assert.Equal(1, transcoder.CallCount);
    }

    [Fact]
    public async Task Changed_version_uses_new_cache_identity_and_reprocesses()
    {
        var source = new FakeSource("wave");
        var transcoder = new FakeTranscoder();
        var service = new AudioPreparationService(source, new MemoryPreparedAudioCache(), transcoder);

        var first = await service.PrepareAsync(Item("same-id", "v1"));
        var second = await service.PrepareAsync(Item("same-id", "v2"));

        Assert.NotEqual(first.CacheKey, second.CacheKey);
        Assert.Equal(2, transcoder.CallCount);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_uncached_transcode()
    {
        var transcoder = new BlockingTranscoder();
        var service = new AudioPreparationService(
            new FakeSource("wave"),
            new MemoryPreparedAudioCache(),
            transcoder);
        var item = Item("same", "v1");

        var first = service.PrepareAsync(item);
        await transcoder.Started.Task;
        var second = service.PrepareAsync(item);
        transcoder.Release.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, transcoder.CallCount);
        Assert.Same(results[0], results[1]);
    }

    [Fact]
    public async Task Failure_removes_partial_cache_entry_and_allows_retry()
    {
        var cache = new TrackingCache();
        var transcoder = new FakeTranscoder { FailuresRemaining = 1 };
        var service = new AudioPreparationService(new FakeSource("wave"), cache, transcoder);
        var item = Item("retry", "v1");

        await Assert.ThrowsAsync<AudioPreparationException>(() => service.PrepareAsync(item));
        var prepared = await service.PrepareAsync(item);

        Assert.NotNull(prepared);
        Assert.Equal(1, cache.RemoveCount);
        Assert.Equal(2, transcoder.CallCount);
    }

    [Fact]
    public async Task Oversized_sources_are_rejected_before_download()
    {
        var source = new FakeSource("large");
        var service = new AudioPreparationService(
            source,
            new MemoryPreparedAudioCache(),
            new FakeTranscoder(),
            new AudioPreparationOptions(MaximumSourceBytes: 3));

        await Assert.ThrowsAsync<AudioPreparationException>(() =>
            service.PrepareAsync(Item("large", "v1", size: 4)));

        Assert.Equal(0, source.OpenCount);
    }

    [Fact]
    public void Signed_grants_validate_until_expiry_and_reject_tampering()
    {
        var service = Grants();
        var token = service.Issue(new PlaybackGrant("prepared-key", Now.AddMinutes(5)));

        Assert.True(service.TryValidate(token, Now, out var grant));
        Assert.Equal("prepared-key", grant!.PreparedAudioKey);
        Assert.False(service.TryValidate(token, Now.AddMinutes(5), out _));
        Assert.False(service.TryValidate(token[..^1] + (token[^1] == 'a' ? 'b' : 'a'), Now, out _));
        Assert.DoesNotContain("https://", token, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Full_stream_returns_200_with_content_headers()
    {
        var cache = await CacheAsync("key", "abcdefghij");
        var grants = Grants();
        var token = grants.Issue(new PlaybackGrant("key", Now.AddMinutes(1)));
        var service = new AudioStreamingService(grants, cache);

        var result = await service.OpenAsync(token, Now, null);

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("audio/mpeg", result.ContentType);
        Assert.Equal(10, result.ContentLength);
        Assert.Null(result.Range);
        Assert.Equal("abcdefghij", await ReadAsync(result.Content));
    }

    [Theory]
    [InlineData("bytes=2-5", 2, 5, "cdef")]
    [InlineData("bytes=6-", 6, 9, "ghij")]
    [InlineData("bytes=-3", 7, 9, "hij")]
    public async Task Valid_ranges_return_206_and_exact_bytes(
        string header,
        long expectedStart,
        long expectedEnd,
        string expectedContent)
    {
        var cache = await CacheAsync("key", "abcdefghij");
        var grants = Grants();
        var token = grants.Issue(new PlaybackGrant("key", Now.AddMinutes(1)));

        var result = await new AudioStreamingService(grants, cache).OpenAsync(token, Now, header);

        Assert.NotNull(result);
        Assert.Equal(206, result.StatusCode);
        Assert.Equal(new AudioRange(expectedStart, expectedEnd), result.Range);
        Assert.Equal(expectedContent.Length, result.ContentLength);
        Assert.Equal(expectedContent, await ReadAsync(result.Content));
    }

    [Theory]
    [InlineData("bytes=20-30")]
    [InlineData("bytes=5-2")]
    [InlineData("items=0-1")]
    [InlineData("bytes=0-1,4-5")]
    public async Task Invalid_ranges_fail_closed(string header)
    {
        var cache = await CacheAsync("key", "abcdefghij");
        var grants = Grants();
        var token = grants.Issue(new PlaybackGrant("key", Now.AddMinutes(1)));

        Assert.Null(await new AudioStreamingService(grants, cache).OpenAsync(token, Now, header));
    }

    [Fact]
    public async Task Invalid_and_expired_tokens_fail_without_opening_content()
    {
        var cache = new TrackingCache();
        var grants = Grants();
        var expired = grants.Issue(new PlaybackGrant("key", Now));
        var service = new AudioStreamingService(grants, cache);

        Assert.Null(await service.OpenAsync("not-a-token", Now, null));
        Assert.Null(await service.OpenAsync(expired, Now, null));
        Assert.Equal(0, cache.GetCount);
    }

    [Fact]
    public async Task Preparation_honors_cancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var service = new AudioPreparationService(
            new FakeSource("wave"),
            new MemoryPreparedAudioCache(),
            new FakeTranscoder());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.PrepareAsync(Item("cancel", "v1"), source.Token));
    }

    private static AudioItem Item(
        string id,
        string version,
        string fileName = "sound.wav",
        string mimeType = "audio/wav",
        long size = 4) =>
        new(id, id, fileName, mimeType, size, Now, version);

    private static HmacPlaybackGrantService Grants() =>
        new(Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());

    private static async Task<MemoryPreparedAudioCache> CacheAsync(string key, string content)
    {
        var cache = new MemoryPreparedAudioCache();
        var bytes = Encoding.UTF8.GetBytes(content);
        await cache.PutAsync(new PreparedAudio(key, "audio/mpeg", bytes.Length,
            _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))));
        return cache;
    }

    private static async Task<string> ReadAsync(Stream stream)
    {
        await using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            return await reader.ReadToEndAsync();
        }
    }

    private sealed class FakeSource(string content) : IAudioSourceContentProvider
    {
        public int OpenCount { get; private set; }

        public Task<Stream> OpenReadAsync(string providerId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false));
        }
    }

    private class FakeTranscoder(string content = "mp3") : IAudioTranscoder
    {
        public int CallCount { get; protected set; }
        public int FailuresRemaining { get; set; }

        public virtual Task<PreparedAudio> TranscodeToMp3Async(
            AudioItem item,
            string cacheKey,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (FailuresRemaining-- > 0)
            {
                throw new AudioPreparationException("transcode failed");
            }

            var bytes = Encoding.UTF8.GetBytes(content);
            return Task.FromResult(new PreparedAudio(cacheKey, "audio/mpeg", bytes.Length,
                _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))));
        }
    }

    private sealed class BlockingTranscoder : FakeTranscoder
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<PreparedAudio> TranscodeToMp3Async(
            AudioItem item,
            string cacheKey,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            var bytes = Encoding.UTF8.GetBytes("mp3");
            return new PreparedAudio(cacheKey, "audio/mpeg", bytes.Length,
                _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
        }
    }

    private sealed class TrackingCache : IPreparedAudioCache
    {
        private readonly Dictionary<string, PreparedAudio> entries = new(StringComparer.Ordinal);
        public int GetCount { get; private set; }
        public int RemoveCount { get; private set; }

        public Task<PreparedAudio?> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetCount++;
            entries.TryGetValue(cacheKey, out var value);
            return Task.FromResult(value);
        }

        public Task PutAsync(PreparedAudio audio, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries[audio.CacheKey] = audio;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
        {
            RemoveCount++;
            entries.Remove(cacheKey);
            return Task.CompletedTask;
        }
    }
}
