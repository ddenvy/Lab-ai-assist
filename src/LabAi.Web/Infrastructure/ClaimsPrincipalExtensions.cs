using System.Security.Claims;

namespace LabAi.Web.Infrastructure;

/// <summary>Extension methods for extracting user identity from authenticated claims.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extracts the user ID from the authenticated principal. Returns null if the user is not
    /// authenticated or the claim is missing/malformed.
    /// </summary>
    public static long? GetUserId(this ClaimsPrincipal principal)
    {
        var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
        if (idClaim is null || !long.TryParse(idClaim.Value, out var id))
            return null;
        return id;
    }
}
