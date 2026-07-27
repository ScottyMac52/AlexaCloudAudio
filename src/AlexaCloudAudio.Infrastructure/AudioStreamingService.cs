using AlexaCloudAudio.Application;

namespace AlexaCloudAudio.Infrastructure;

public sealed class AudioStreamingService : IAudioStreamingService
{
    private readonly IPlaybackGrantService grants;
    private readonly IPreparedAudioCache cache;

    public AudioStreamingService(IPlaybackGrantService grants, IPreparedAudioCache cache)
    {
        this.grants = grants ?? throw new ArgumentNullException(nameof(grants));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<AudioStreamResult?> OpenAsync(
        string token,
        DateTimeOffset now,
        string? rangeHeader,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!grants.TryValidate(token, now, out var grant) || grant is null)
        {
            return null;
        }

        var audio = await cache.GetAsync(grant.PreparedAudioKey, cancellationToken).ConfigureAwait(false);
        if (audio is null)
        {
            return null;
        }

        var requestedRange = ParseRange(rangeHeader, audio.Length);
        if (rangeHeader is not null && requestedRange is null)
        {
            return null;
        }

        var stream = await audio.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (requestedRange is null)
        {
            return new AudioStreamResult(200, audio.ContentType, audio.Length, null, stream);
        }

        await using (stream.ConfigureAwait(false))
        {
            if (!stream.CanSeek)
            {
                throw new InvalidOperationException("Prepared audio streams must support seeking for range responses.");
            }

            stream.Position = requestedRange.Start;
            var bytes = new byte[checked((int)requestedRange.Length)];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Prepared audio ended before the requested range was read.");
                }

                offset += read;
            }

            return new AudioStreamResult(
                206,
                audio.ContentType,
                requestedRange.Length,
                requestedRange,
                new MemoryStream(bytes, writable: false));
        }
    }

    public static AudioRange? ParseRange(string? header, long length)
    {
        if (header is null)
        {
            return null;
        }

        if (length <= 0 || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = header[6..].Trim();
        if (value.Contains(',', StringComparison.Ordinal))
        {
            return null;
        }

        var separator = value.IndexOf('-');
        if (separator < 0)
        {
            return null;
        }

        var startText = value[..separator].Trim();
        var endText = value[(separator + 1)..].Trim();
        if (startText.Length == 0)
        {
            if (!long.TryParse(endText, out var suffixLength) || suffixLength <= 0)
            {
                return null;
            }

            suffixLength = Math.Min(suffixLength, length);
            return new AudioRange(length - suffixLength, length - 1);
        }

        if (!long.TryParse(startText, out var start) || start < 0 || start >= length)
        {
            return null;
        }

        var end = length - 1;
        if (endText.Length > 0 && (!long.TryParse(endText, out end) || end < start))
        {
            return null;
        }

        end = Math.Min(end, length - 1);
        return new AudioRange(start, end);
    }
}
