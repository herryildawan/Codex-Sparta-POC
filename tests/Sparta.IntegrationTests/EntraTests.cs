using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Sparta.Security;
using Sparta.WebApi.DatabaseUpdate;
using Sparta.WebApi.JWT;

static class EntraTests {
    public static async Task Run(string contentRoot, Action<bool, string> check) {
        var tenant = Guid.NewGuid().ToString();
        var audience = Guid.NewGuid().ToString();
        var oid = Guid.NewGuid().ToString();
        var issuer = $"https://login.microsoftonline.com/{tenant}/v2.0";
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString() };
        var metadata = new OpenIdConnectConfiguration { Issuer = issuer };
        metadata.SigningKeys.Add(key);
        await using var db = new SecurityFactory().CreateDbContext([]);
        var reader = await db.Users.Include(x => x.Roles).SingleAsync(x => x.UserName == "sales.reader");
        var user = db.CreateProxy<ApplicationUser>();
        user.UserName = "entra-test-" + Guid.NewGuid().ToString("N");
        user.IsActive = true;
        user.SetPassword(Guid.NewGuid().ToString("N"));
        foreach(var role in reader.Roles) user.Roles.Add(role);
        db.Users.Add(user);
        var login = db.CreateProxy<ApplicationUserLoginInfo>();
        login.LoginProviderName = EntraAuthentication.Scheme;
        login.ProviderUserKey = EntraAuthentication.Key(tenant, oid);
        login.User = user;
        db.UserLogins.Add(login);
        await db.SaveChangesAsync();
        try {
            using var app = new WebApplicationFactory<Sparta.WebApi.Program>().WithWebHostBuilder(b => b
                .UseEnvironment("Development").UseContentRoot(contentRoot)
                .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["Authentication:Entra:Enabled"] = "true",
                    ["Authentication:Entra:TenantId"] = tenant,
                    ["Authentication:Entra:ClientId"] = audience,
                    ["Authentication:Entra:SwaggerClientId"] = audience,
                    ["Authentication:Entra:RequiredScope"] = "access_as_user",
                    ["Authentication:Local:Enabled"] = "false"
                }))
                .ConfigureServices(s => s.PostConfigure<JwtBearerOptions>(EntraAuthentication.Scheme, options => {
                    // Replace only discovery/signing-key transport. The real bearer validation and XAF mapping run.
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                })));
            using var client = app.CreateClient();
            string Token(string? tokenOid = null, string? tokenTenant = null, string? tokenAudience = null,
                string? scope = "access_as_user", string? tokenIssuer = null, bool expired = false, SecurityKey? signingKey = null) {
                var claims = new List<Claim> {
                    new("tid", tokenTenant ?? tenant), new("oid", tokenOid ?? oid), new("ver", "2.0"),
                    new("sub", "untrusted-subject"), new("preferred_username", "admin"), new("roles", "PlatformAdministrator")
                };
                if(scope != null) claims.Add(new("scp", scope));
                return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(tokenIssuer ?? issuer, tokenAudience ?? audience,
                    claims, DateTime.UtcNow.AddHours(-2), expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(5),
                    new SigningCredentials(signingKey ?? key, SecurityAlgorithms.RsaSha256)));
            }
            async Task<(HttpStatusCode Status, string Body)> Get(string token, string path = "/api/odata/Customer") {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await client.SendAsync(request);
                return (response.StatusCode, await response.Content.ReadAsStringAsync());
            }
            var valid = await Get(Token());
            check(valid.Status == HttpStatusCode.OK && valid.Body.Contains("CUST-001"), "Entra signed access token maps to linked XAF sales reader");
            var inventory = await Get(Token(), "/api/odata/Product");
            check(inventory.Status == HttpStatusCode.OK && !inventory.Body.Contains("PROD-001"), "Entra username and role claims cannot elevate XAF permissions");
            check((await Get(Token(tokenAudience: Guid.NewGuid().ToString()))).Status == HttpStatusCode.Unauthorized, "Entra wrong audience rejected");
            check((await Get(Token(tokenTenant: Guid.NewGuid().ToString()))).Status == HttpStatusCode.Unauthorized, "Entra wrong tenant rejected");
            check((await Get(Token(tokenIssuer: "https://example.invalid"))).Status == HttpStatusCode.Unauthorized, "Entra wrong issuer rejected");
            check((await Get(Token(expired: true))).Status == HttpStatusCode.Unauthorized, "Entra expired access token rejected");
            check((await Get(Token(scope: "unrelated"))).Status == HttpStatusCode.Unauthorized, "Entra wrong scope rejected");
            check((await Get(Token(scope: null))).Status == HttpStatusCode.Unauthorized, "Entra token without delegated scope rejected");
            using var otherRsa = RSA.Create(2048);
            check((await Get(Token(signingKey: new RsaSecurityKey(otherRsa) { KeyId = key.KeyId }))).Status == HttpStatusCode.Unauthorized, "Entra forged signature rejected");
            var unlinked = await Get(Token(tokenOid: Guid.NewGuid().ToString()));
            check(unlinked.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, "Unlinked Entra identity denied");
            using var passwordResponse = await client.PostAsync("/api/Authentication/Authenticate", new StringContent("{\"userName\":\"admin\",\"password\":\"unused\"}", System.Text.Encoding.UTF8, "application/json"));
            check(passwordResponse.StatusCode == HttpStatusCode.NotFound, "Entra-only mode disables password token endpoint");
            var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");
            check(swagger.Contains("authorizationCode") && swagger.Contains("access_as_user"), "OpenAPI documents Entra authorization code flow and scope");
            user.IsActive = false;
            await db.SaveChangesAsync();
            var disabled = await Get(Token());
            check(disabled.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, "Disabled XAF user denied despite valid Entra token");
        } finally {
            db.UserLogins.Remove(login);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }
}
