using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlexaCloudAudio.Application;

namespace AlexaCloudAudio.Infrastructure;

public sealed class HmacPlaybackGrantService : IPlaybackGrantService
{
    private readonly byte[] signingKey;

    public HmacPlaybackGrantService(byte[] signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        if (signingKey.Length < 32)
        {
            throw new ArgumentException("The playback signing key must contain at least 32 bytes.", nameof(signingKey));
        }

        this.signingKey = signingKey.ToArray();
    }

    public string Issue(PlaybackGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.PreparedAudioKey);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new GrantPayload(grant.PreparedAudioKey, grant.ExpiresAt.ToUnixTimeSeconds()));
        var signature = HMACSHA256.HashData(signingKey, payload);
        return $"{Base64UrlEncode(payload)}.{Base64UrlEncode(signature)}";
    }

    public bool TryValidate(string token, DateTimeOffset now, out PlaybackGrant? grant)
    {
        grant = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 2 || !TryBase64UrlDecode(parts[0], out var payload) || !TryBase64UrlDecode(parts[1], out var signature))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(signingKey, payload);
        if (signature.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(signature, expected))
        {
            return false;
        }

        try
        {
            var decoded = JsonSerializer.Deserialize<GrantPayload>(payload);
            if (decoded is null || string.IsNullOrWhiteSpace(decoded.Key))
            {
                return false;
            }

            var expires = DateTimeOffset.FromUnixTimeSeconds(decoded.Expires);
            if (expires <= now)
            {
                return false;
            }

            grant = new PlaybackGrant(decoded.Key, expires);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record GrantPayload(string Key, long Expires);
}
