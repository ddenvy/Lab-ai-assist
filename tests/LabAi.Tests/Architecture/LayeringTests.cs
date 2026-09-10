using System.Reflection;

namespace LabAi.Tests.Architecture;

/// <summary>
/// Clean Architecture guard (AGENT.md 2.1): dependencies must point inward. These assertions
/// stay meaningful as code lands in later milestones — a stray EF Core or Semantic Kernel
/// reference in Domain fails the build here rather than in review.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly Domain = typeof(LabAi.Domain.Marker).Assembly;
    private static readonly Assembly Application = typeof(LabAi.Application.Marker).Assembly;

    [Fact]
    public void Domain_ReferencesNoOtherProject()
    {
        ReferencedProjectNames(Domain).Should().BeEmpty();
    }

    [Fact]
    public void Domain_ReferencesNoFrameworkPackages()
    {
        // BCL only: anything from EF Core, Semantic Kernel, ASP.NET or Serilog is a violation.
        var forbidden = Domain.GetReferencedAssemblies()
            .Where(a => a.Name is not null && (
                a.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                a.Name.StartsWith("Microsoft.SemanticKernel", StringComparison.Ordinal) ||
                a.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                a.Name.StartsWith("Serilog", StringComparison.Ordinal)))
            .Select(a => a.Name)
            .ToArray();

        forbidden.Should().BeEmpty();
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructureOrWeb()
    {
        // Only the forbidden direction is asserted. A positive "must reference Domain" check is
        // unreliable: the compiler omits assembly references that the code never actually uses,
        // so the metadata list is empty until Application touches a Domain type.
        ReferencedProjectNames(Application)
            .Should()
            .NotContain(["LabAi.Infrastructure", "LabAi.Web"]);
    }

    private static string[] ReferencedProjectNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && n.StartsWith("LabAi.", StringComparison.Ordinal))
            .Select(n => n!)
            .ToArray();
}
