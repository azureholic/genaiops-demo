namespace GenAIOps.Application;

/// <summary>
/// Identifies the application assembly and anchors its dependency on the domain layer.
/// </summary>
public sealed class AssemblyMarker
{
    public static Type DomainAssemblyMarker => typeof(Domain.AssemblyMarker);
}
