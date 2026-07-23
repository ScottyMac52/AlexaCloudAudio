using System.Text;

namespace AlexaCloudAudio.Application;

public static class AudioNameNormalizer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".flac", ".m4a", ".mp3", ".ogg", ".wav"
    };

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var candidate = value.Trim();
        var extension = Path.GetExtension(candidate);
        if (extension.Length > 0)
        {
            candidate = candidate[..^extension.Length];
        }

        var builder = new StringBuilder(candidate.Length);
        var pendingSpace = false;

        foreach (var character in candidate)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = builder.Length > 0;
            }
        }

        return builder.ToString();
    }

    public static bool IsSupportedFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return SupportedExtensions.Contains(Path.GetExtension(fileName));
    }
}
