using Xunit;

namespace AlexaCloudAudio.Application.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Application_references_only_domain_within_solution()
    {
        Assert.Equal(typeof(global::AlexaCloudAudio.Domain.AssemblyMarker), global::AlexaCloudAudio.Application.AssemblyMarker.DomainAssembly);

        var references = typeof(global::AlexaCloudAudio.Application.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Where(static name => name is not null && name.StartsWith("AlexaCloudAudio.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] expected = ["AlexaCloudAudio.Domain"];
        Assert.Equal(expected, references);
    }
}
