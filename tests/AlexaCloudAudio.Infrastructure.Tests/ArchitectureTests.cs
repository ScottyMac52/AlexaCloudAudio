using Xunit;

namespace AlexaCloudAudio.Infrastructure.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Infrastructure_references_only_application_and_domain_within_solution()
    {
        Assert.Equal(typeof(global::AlexaCloudAudio.Application.AssemblyMarker), global::AlexaCloudAudio.Infrastructure.AssemblyMarker.ApplicationAssembly);
        Assert.Equal(typeof(global::AlexaCloudAudio.Domain.AssemblyMarker), global::AlexaCloudAudio.Infrastructure.AssemblyMarker.DomainAssembly);

        var references = typeof(global::AlexaCloudAudio.Infrastructure.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Where(static name => name is not null && name.StartsWith("AlexaCloudAudio.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] expected = ["AlexaCloudAudio.Application", "AlexaCloudAudio.Domain"];
        Assert.Equal(expected, references);
    }
}
