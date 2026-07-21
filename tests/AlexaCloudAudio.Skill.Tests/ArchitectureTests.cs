using Xunit;

namespace AlexaCloudAudio.Skill.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Skill_references_only_application_and_infrastructure_directly()
    {
        Assert.Equal(typeof(global::AlexaCloudAudio.Application.AssemblyMarker), global::AlexaCloudAudio.Skill.AssemblyMarker.ApplicationAssembly);
        Assert.Equal(typeof(global::AlexaCloudAudio.Infrastructure.AssemblyMarker), global::AlexaCloudAudio.Skill.AssemblyMarker.InfrastructureAssembly);

        var references = typeof(global::AlexaCloudAudio.Skill.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Where(static name => name is not null && name.StartsWith("AlexaCloudAudio.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["AlexaCloudAudio.Application", "AlexaCloudAudio.Infrastructure"], references);
    }
}
