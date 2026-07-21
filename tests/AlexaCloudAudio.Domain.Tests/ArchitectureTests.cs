using System.Reflection;
using Xunit;

namespace AlexaCloudAudio.Domain.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_does_not_reference_other_solution_projects()
    {
        var references = typeof(Domain.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Where(static name => name is not null && name.StartsWith("AlexaCloudAudio.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(references);
    }
}
