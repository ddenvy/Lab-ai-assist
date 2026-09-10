using LabAi.Domain.Abstractions;
using LabAi.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LabAi.Web.Endpoints;

/// <summary>
/// Cookie login for both the browser pages and the JSON API. A Blazor Server circuit is a
/// WebSocket that cannot carry an Authorization header, so the cookie is the one scheme that
/// covers HTTP endpoints and the interactive circuit alike — and it yields the authenticated
/// user that becomes <c>ActorUserId</c> in the AI audit journal.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", static async (
            LoginRequest request,
            IAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Username)] = ["Username and password are required."],
                });
            }

            var result = await auth.LoginAsync(request.Username, request.Password, cancellationToken);
            if (!result.Succeeded)
            {
                // The detail is the AuthService's single generic message: anything more specific
                // would let an unauthenticated caller enumerate the staff list.
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, detail: result.Error);
            }

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                result.ToClaimsPrincipal());

            return Results.Ok(new LoginResponse(result.UserId, result.Username, result.FullName, result.Role.ToString()));
        });

        endpoints.MapPost("/api/auth/logout", static async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        // Browser form login (Login.razor posts here). Antiforgery stays ON: the form carries the
        // token, and a redirect-based flow must not be fireable cross-site. GET /login is the
        // component route; there is no conflict because the paths differ only by method.
        endpoints.MapPost("/login", static async (
            [FromForm] string? username,
            [FromForm] string? password,
            IAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Results.Redirect("/login?error=1");
            }

            var result = await auth.LoginAsync(username, password, cancellationToken);
            if (!result.Succeeded)
            {
                return Results.Redirect("/login?error=1");
            }

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                result.ToClaimsPrincipal());

            // Redirect to the documents page after login — it requires authentication and shows
            // the corpus immediately, giving the user a clear next step.
            return Results.Redirect("/documents");
        });

        return endpoints;
    }
}

/// <summary>Body of <c>POST /api/auth/login</c>.</summary>
public sealed record LoginRequest(string? Username, string? Password);

/// <summary>Response of <c>POST /api/auth/login</c>.</summary>
public sealed record LoginResponse(long UserId, string Username, string FullName, string Role);
