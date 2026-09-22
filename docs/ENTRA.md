# Microsoft Entra ID authentication

Sparta remains a standalone Web API. Microsoft.Identity.Web validates Entra access tokens; the XAF authentication provider resolves the existing user and enforces roles from SpartaSecurity. No Blazor, WinForms, Middle Tier, cookie login, or server sign-in callback is required.

## Local configuration

The supplied tenant `5e56b47d-4cbe-4d80-83f9-d471db3833c6` and API application `98e9dfb4-f878-4463-990e-c057dda38670` are configured in local User Secrets under `Authentication:Entra`. Entra is enabled locally. Local password authentication remains enabled for POC accounts; set `Authentication:Local:Enabled=false` for Entra-only access. This disables local bearer validation and the password-token endpoint. No client secret is required for Scalar PKCE or API token validation.

The application ID also provisionally serves as `SwaggerClientId`. The configuration key retains its historical name but now supplies Scalar's browser client ID. A separate SPA registration can replace it without changing the API audience. The configured scope name is provisionally `access_as_user`; confirm it against the registration or set `Authentication:Entra:RequiredScope` to the actual scope name, e.g. `WebApi`.

## App registration requirements

1. Use a single-tenant registration. Under Expose an API, use Application ID URI `api://98e9dfb4-f878-4463-990e-c057dda38670` and expose the delegated scope `access_as_user` (or configure the existing scope name).
2. Set the API manifest's `api.requestedAccessTokenVersion` to `2`. Sparta accepts v2 access tokens with the API client GUID as audience.
3. Under Authentication, add the exact Scalar URL as a **Single-page application** redirect URI, for example `http://localhost:5180/scalar/`. If using an Aspire or HTTPS address, register that exact Scalar URL too. `/signin-oidc` is not used by this API.
4. Grant the Scalar browser client the delegated API permission and consent as required by tenant policy. If using separate registrations, set `SwaggerClientId` to the SPA application ID.
5. Restart the API, open `/scalar/`, choose Entra authentication, select the delegated scope, and sign in. Scalar uses authorization code with PKCE.

Portal configuration and real interactive sign-in have not yet been verified. A tenant/application ID alone cannot establish whether scopes, redirect URIs, or consent are configured.

## Link an existing Sparta account

Obtain the **user Object ID in this tenant**, not the application/service-principal Object ID. Choose the Sparta account whose existing roles should apply. From the repository root:

```powershell
dotnet run --project src/Sparta.Api -c Release -- --link-entra sales.reader 5e56b47d-4cbe-4d80-83f9-d471db3833c6 <entra-user-object-id>
```

This administrative CLI uses the configured Security database credentials. It writes the existing XAF login-info table with provider `Entra` and key `<tenant-guid>:<user-object-guid>`. It is idempotent for the same account and refuses reassignment to another account. No migration or new database is needed. Treat database/CLI access as privileged administrative access.

Unlinked or inactive users are denied. There is no automatic registration, email matching, group-to-role mapping, or privilege grant. XAF role changes apply on subsequent requests. Attribution continues to use the stable XAF user GUID and local username snapshot. Entra app/role claims do not grant business permissions. Machine-to-machine tokens are intentionally unsupported in this milestone; a delegated scope is required.

## Verification

The integration suite uses ephemeral RSA signing keys and static discovery metadata in its test host. Production uses Microsoft's HTTPS discovery and signing-key rotation. Tests exercise the actual bearer validation and XAF mapping, including audience, issuer, tenant, signature, expiry, scope, linkage, inactive users, RBAC, and password-login disablement. This does not replace a real Entra browser sign-in test.

References: [DevExpress Web API OAuth](https://docs.devexpress.com/eXpressAppFramework/403505/backend-web-api-service/authentication-in-web-api-applications/enable-oauth2-azure-authentication), [Microsoft PKCE flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow), [access-token claims](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference).
