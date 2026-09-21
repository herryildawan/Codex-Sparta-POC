using System.Security.Claims;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Sparta.Security;

namespace Sparta.WebApi.JWT;

public static class EntraAuthentication
{
    public const string Scheme = "Entra";
    public static string Key(string tenant, string objectId)
    {
        return $"{Guid.Parse(tenant):D}:{Guid.Parse(objectId):D}";
    }

    public static Task ValidateToken(TokenValidatedContext context, IConfiguration configuration)
    {
        var principal = context.Principal!;
        var tenant = principal.FindFirstValue("tid");
        var objectId = principal.FindFirstValue("oid");
        var scope = configuration["Authentication:Entra:RequiredScope"] ?? "access_as_user";
        
        if (!Guid.TryParse(tenant, out var tenantId) ||
           tenantId != Guid.Parse(configuration["Authentication:Entra:TenantId"]!) ||
           !Guid.TryParse(objectId, out _) || principal.FindFirstValue("ver") != "2.0" ||
           !(principal.FindFirstValue("scp") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope))
        {
            context.Fail("A delegated Sparta access token from the configured tenant is required.");
            return Task.CompletedTask;
        }
        
        // Use a stable, tenant-qualified identity; never match accounts by email/UPN.
        var identity = (ClaimsIdentity)principal.Identity!;
        foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).Concat(identity.FindAll("sub")).ToArray())
        {
            identity.RemoveClaim(claim);
        }
            
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Key(tenant!, objectId!)));
        return Task.CompletedTask;
    }

    public static ApplicationUser ResolveUser(IObjectSpace objectSpace, ClaimsPrincipal principal)
    {
        var key = Key(principal.FindFirstValue("tid")!, principal.FindFirstValue("oid")!);
        var login = objectSpace.FirstOrDefault<ApplicationUserLoginInfo>(x => x.LoginProviderName == Scheme && x.ProviderUserKey == key);
        
        if (login?.User is not { IsActive: true } user)
            throw new AuthenticationException("Entra identity is not linked to an active Sparta user.");
        return user;
    }
}

public sealed class EntraXafAuthenticationProvider(UserManager userManager) : IAuthenticationProviderV2
{
    public object Authenticate(IObjectSpace objectSpace)
    {
        var principal = userManager.GetCurrentPrincipal();
        if (principal?.Identity is { IsAuthenticated: true, AuthenticationType: EntraAuthentication.Scheme })
            return new UserResult<ApplicationUser>(EntraAuthentication.ResolveUser(objectSpace, (ClaimsPrincipal)principal));
        
        return null!;
    }
}
