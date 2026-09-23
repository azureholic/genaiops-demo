using System.Reflection;

namespace GenAIOps.UnitTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_has_no_dependencies_on_other_solution_layers()
    {
        Assembly domain = typeof(Domain.AssemblyMarker).Assembly;

        string[] solutionReferences = GetSolutionReferences(domain);

        Assert.Empty(solutionReferences);
    }

    [Fact]
    public void Application_depends_on_domain_but_not_outward_layers()
    {
        Assembly application = typeof(Application.AssemblyMarker).Assembly;

        string[] solutionReferences = GetSolutionReferences(application);

        Assert.Contains("GenAIOps.Domain", solutionReferences);
        Assert.DoesNotContain("GenAIOps.Infrastructure", solutionReferences);
        Assert.DoesNotContain("GenAIOps.Api", solutionReferences);
        Assert.DoesNotContain(solutionReferences, name => name.StartsWith("GenAIOps.Workers.", StringComparison.Ordinal));
    }

    private static string[] GetSolutionReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("GenAIOps.", StringComparison.Ordinal))
            .ToArray();
}
