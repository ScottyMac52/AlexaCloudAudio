using System.Collections.Concurrent;
using System.Diagnostics;
using AlexaCloudAudio.Application;
using AlexaCloudAudio.Domain;

namespace AlexaCloudAudio.Infrastructure;

public sealed class MemoryPreparedAudioCache : IPreparedAudioCache
{
    private readonly ConcurrentDictionary<string, PreparedAudio> entries = new(StringComparer.Ordinal);

    public Task<PreparedAudio?> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        entries.TryGetValue(cacheKey, out var audio);
        return Task.FromResult(audio);
    }

    public Task PutAsync(PreparedAudio audio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        cancellationToken.ThrowIfCancellationRequested();
        entries[audio.CacheKey] = audio;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        entries.TryRemove(cacheKey, out _);
        return Task.CompletedTask;
    }
}

public sealed class OneDriveAudioSourceContentProvider : IAudioSourceContentProvider
{
    private readonly IOneDriveDownloadUrlProvider downloads;
    private readonly HttpClient httpClient;

    public OneDriveAudioSourceContentProvider(IOneDriveDownloadUrlProvider downloads, HttpClient httpClient)
    {
        this.downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<Stream> OpenReadAsync(string providerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var downloadUri = await downloads.GetDownloadUriAsync(providerId, cancellationToken).ConfigureAwait(false);
        var bytes = await httpClient.GetByteArrayAsync(downloadUri, cancellationToken).ConfigureAwait(false);
        return new MemoryStream(bytes, writable: false);
    }
}

public sealed record FfmpegOptions(string ExecutablePath = "ffmpeg", int AudioBitrateKbps = 128, int SampleRateHz = 44100);

public sealed class FfmpegAudioTranscoder : IAudioTranscoder
{
    private readonly FfmpegOptions options;

    public FfmpegAudioTranscoder(FfmpegOptions? options = null)
    {
        this.options = options ?? new FfmpegOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(this.options.ExecutablePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.options.AudioBitrateKbps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.options.SampleRateHz);
    }

    public async Task<PreparedAudio> TranscodeToMp3Async(
        AudioItem item,
        string cacheKey,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var workDirectory = Path.Combine(Path.GetTempPath(), "AlexaCloudAudio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        var inputPath = Path.Combine(workDirectory, "source" + Path.GetExtension(item.SourceFileName));
        var outputPath = Path.Combine(workDirectory, "prepared.mp3");

        try
        {
            await using (var input = File.Create(inputPath))
            {
                await source.CopyToAsync(input, cancellationToken).ConfigureAwait(false);
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = options.ExecutablePath,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-nostdin");
            process.StartInfo.ArgumentList.Add("-y");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(inputPath);
            process.StartInfo.ArgumentList.Add("-vn");
            process.StartInfo.ArgumentList.Add("-codec:a");
            process.StartInfo.ArgumentList.Add("libmp3lame");
            process.StartInfo.ArgumentList.Add("-b:a");
            process.StartInfo.ArgumentList.Add($"{options.AudioBitrateKbps}k");
            process.StartInfo.ArgumentList.Add("-ar");
            process.StartInfo.ArgumentList.Add(options.SampleRateHz.ToString(System.Globalization.CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add(outputPath);

            if (!process.Start())
            {
                throw new AudioPreparationException("Audio transcoding could not be started.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new AudioPreparationException("Audio transcoding failed.");
            }

            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false);
            return new PreparedAudio(cacheKey, "audio/mpeg", bytes.LongLength,
                _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
