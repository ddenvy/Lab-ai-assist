using System.Reflection;

namespace LabAi.Tests.Architecture;

/// <summary>
/// Clean Architecture guard (AGENT.md 2.1): dependencies must point inward. These assertions
/// stay meaningful as code lands in later milestones — a stray EF Core or Semantic Kernel
/// reference in Domain fails the build here rather than in review.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly Domain = typeof(LabAi.Domain.Abstractions.IGroundedChatClient).Assembly;
    private static readonly Assembly Application = typeof(LabAi.Application.Auth.AuthService).Assembly;

    [Fact]
    public void Domain_ReferencesNoOtherProject()
    {
        ReferencedProjectNames(Domain).Should().BeEmpty();
    }

    [Fact]
    public void Domain_ReferencesNoFrameworkPackages()
    {
        ForbiddenFrameworkReferences(Domain).Should().BeEmpty();
    }

    [Fact]
    public void Application_ReferencesNoFrameworkPackages()
    {
        // Application is BCL-only by design: chunking, masking, hashing, cosine and prompt composition
        // are all pure. ILogger<T> here would be the first crack, and it is cheap to catch it now.
        ForbiddenFrameworkReferences(Application).Should().BeEmpty();
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructureOrWeb()
    {
        // Only the forbidden direction is asserted. A positive "must reference Domain" check stays out
        // even though Application now does use Domain types: the compiler omits assembly references the
        // code never touches, so such an assertion would start failing for a harmless reason the moment
        // the last Domain usage moved elsewhere.
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

    private static string[] ForbiddenFrameworkReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && (
                n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                n.StartsWith("Microsoft.SemanticKernel", StringComparison.Ordinal) ||
                n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                n.StartsWith("Serilog", StringComparison.Ordinal)))
            .Select(n => n!)
            .ToArray();
}
