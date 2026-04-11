using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// Integration-test replacement for ASP.NET Core's default
/// <see cref="IPolicyEvaluator"/>. Registered in
/// <see cref="SchemeWeaverWebApplicationFactory"/> via
/// <c>ConfigureTestServices</c>, where the last registration wins, so all
/// <c>[Authorize]</c> attributes on protected management API endpoints resolve
/// to a successful result for a synthetic admin principal.
///
/// <para>
/// This sidesteps the fragile alternative of replacing Umbraco's
/// backoffice authentication handler at the scheme level — that requires
/// mutating <c>AuthenticationOptions.SchemeMap</c>, which throws on duplicates
/// and has no public removal API.
/// </para>
/// </summary>
public sealed class TestPolicyEvaluator : IPolicyEvaluator
{
    private const string TestSchemeName = "IntegrationTest";

    public Task<AuthenticateResult> AuthenticateAsync(
        AuthorizationPolicy policy,
        HttpContext context)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "-1"),
            new Claim(ClaimTypes.Name, "integration-test-admin"),
            new Claim(ClaimTypes.Role, "admin"),
        };

        var identity = new ClaimsIdentity(claims, TestSchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(
            principal,
            new AuthenticationProperties(),
            TestSchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    public Task<PolicyAuthorizationResult> AuthorizeAsync(
        AuthorizationPolicy policy,
        AuthenticateResult authenticationResult,
        HttpContext context,
        object? resource)
    {
        return Task.FromResult(PolicyAuthorizationResult.Success());
    }
}
