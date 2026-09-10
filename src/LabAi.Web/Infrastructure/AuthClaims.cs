using System.Globalization;
using System.Security.Claims;
using LabAi.Domain.Abstractions;

namespace LabAi.Web.Infrastructure;

/// <summary>
/// Builds the cookie principal from a successful login. The <c>NameIdentifier</c> claim carries the
/// database user id, which is what every audited action must attribute itself to — the username is
/// mutable, the id is not.
/// </summary>
public static class AuthClaims
{
    public static ClaimsPrincipal ToClaimsPrincipal(this AuthResult result)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, result.Username),
            new Claim(ClaimTypes.GivenName, result.FullName),
            new Claim(ClaimTypes.Role, result.Role.ToString()),
        ],
        authenticationType: "LabAi.Cookie");

        return new ClaimsPrincipal(identity);
    }
}
