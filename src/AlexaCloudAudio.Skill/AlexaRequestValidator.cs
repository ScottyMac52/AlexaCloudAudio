namespace AlexaCloudAudio.Skill;

public sealed class AlexaRequestValidator : IAlexaRequestValidator
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(150);

    public AlexaValidationResult Validate(AlexaRequestEnvelope envelope, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!envelope.SignatureValid)
        {
            return AlexaValidationResult.Invalid("Invalid signature.");
        }

        if (!StringComparer.Ordinal.Equals(envelope.Version, "1.0"))
        {
            return AlexaValidationResult.Invalid("Unsupported request version.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Request.Type))
        {
            return AlexaValidationResult.Invalid("Missing request type.");
        }

        if ((now - envelope.Timestamp).Duration() > AllowedClockSkew)
        {
            return AlexaValidationResult.Invalid("Stale request.");
        }

        return AlexaValidationResult.Valid();
    }
}
