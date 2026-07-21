namespace AlexaCloudAudio.Infrastructure;

public static class AssemblyMarker
{
    public static Type ApplicationAssembly => typeof(global::AlexaCloudAudio.Application.AssemblyMarker);

    public static Type DomainAssembly => typeof(global::AlexaCloudAudio.Domain.AssemblyMarker);
}
