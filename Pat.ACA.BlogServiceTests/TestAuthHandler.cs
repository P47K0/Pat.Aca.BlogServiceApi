using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pat.ACA.BlogServiceTests
{
    /// <summary>
    /// Stands in for Microsoft.Identity.Web's real Azure AD JWT bearer auth in
    /// integration tests — no real tenant/token available in this environment.
    /// Reads test-only headers instead of a bearer token: if RolesHeaderName is
    /// present, authenticates the request with those roles (comma-separated);
    /// if absent, the request stays unauthenticated (so the 401 paths are still
    /// exercisable). Registered as the app's default scheme only inside the
    /// test host, via WebApplicationFactory's ConfigureTestServices — the real
    /// Microsoft.Identity.Web wiring in Program.cs is untouched.
    /// </summary>
    public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestScheme";
        public const string RolesHeaderName = "X-Test-Roles";

        // Feeds ApiSecurity.GetWriteRateLimitPartitionKey's "oid" claim lookup
        // — lets each test client get its own rate-limit partition instead of
        // all sharing one "test-caller" budget across a whole fixture.
        public const string CallerIdHeaderName = "X-Test-Caller-Id";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeaderName, out var rolesHeader))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var callerId = Request.Headers.TryGetValue(CallerIdHeaderName, out var callerIdHeader)
                ? callerIdHeader.ToString()
                : "test-caller";

            var roles = rolesHeader.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var claims = new List<Claim>
            {
                new("oid", callerId),
                new(ClaimTypes.NameIdentifier, callerId)
            };
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
