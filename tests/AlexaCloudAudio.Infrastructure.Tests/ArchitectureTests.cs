using Xunit;

namespace AlexaCloudAudio.Infrastructure.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Infrastructure_references_only_application_and_domain_within_solution()
    {
        var references = typeof(global::AlexaCloudAudio.Infrastructure.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Where(static name => name is not null && name.StartsWith("AlexaCloudAudio.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["AlexaCloudAudio.Application", "AlexaCloudAudio.Domain"], references);
    }
}
