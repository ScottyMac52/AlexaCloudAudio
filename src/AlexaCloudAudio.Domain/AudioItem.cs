namespace AlexaCloudAudio.Domain;

public sealed record AudioItem
{
    public AudioItem(
        string providerId,
        string displayName,
        string sourceFileName,
        string mimeType,
        long sizeBytes,
        DateTimeOffset modifiedAt,
        string version,
        IEnumerable<string>? aliases = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        ProviderId = providerId;
        DisplayName = displayName;
        SourceFileName = sourceFileName;
        MimeType = mimeType;
        SizeBytes = sizeBytes;
        ModifiedAt = modifiedAt;
        Version = version;
        Aliases = (aliases ?? []).Select(static alias => alias.Trim()).Where(static alias => alias.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string ProviderId { get; }
    public string DisplayName { get; }
    public string SourceFileName { get; }
    public string MimeType { get; }
    public long SizeBytes { get; }
    public DateTimeOffset ModifiedAt { get; }
    public string Version { get; }
    public IReadOnlyList<string> Aliases { get; }
}
