# API documentation with Scalar

Sparta uses Scalar as the interactive API reference and Swashbuckle as the OpenAPI document generator. The API reference is currently available only in the Development environment.

## Current implementation

| Concern | Implementation |
|---|---|
| Interactive reference | Scalar.AspNetCore 2.17.6 |
| OpenAPI generation | Swashbuckle.AspNetCore 6.9.0 |
| Scalar route | `/scalar` |
| OpenAPI document | `/swagger/v1/swagger.json` |
| Environments | Development only |
| Development root | `/` redirects to `/scalar/` |
| Scalar Agent | Disabled |
| Authentication | Local bearer token and optional Entra authorization code with PKCE |

Start the API and open the reference:

```powershell
dotnet run --project src/Sparta.Api
```

- API reference: `http://localhost:5180/scalar/`
- OpenAPI JSON: `http://localhost:5180/swagger/v1/swagger.json`

Opening `http://localhost:5180/` in Development redirects directly to the Scalar API reference. The redirect is not mapped in Production.

Scalar reads the OpenAPI document produced by Swashbuckle. Removing Swagger UI does not remove or replace Swashbuckle because the JSON document is still required.

## Navigation

`ScalarOperationTagsFilter` normalizes tags that would otherwise be derived from mixed-purpose controller names. `ScalarTagGroupsFilter` emits Scalar's `x-tagGroups` extension and declares all generated tags at document level.

The navigation is organized as follows:

```text
Platform
|- Authentication
|- Session
|- Audit
|- Localization
|- MediaFile
`- Metadata

Sales
|- Customer
|- SalesOrder
|- SalesOrderLine
`- Sales Operations

Inventory
|- Product
|- Warehouse
|- StockMovement
|- Product Commands
|- Inventory Commands
`- Inventory Operations
```

Tags not covered by the known groups are placed in an `Other` group so that new endpoints do not silently disappear from the reference. When adding a new public controller or business object, assign it to an intentional group and verify the generated document.

## Authentication

The OpenAPI document can advertise two security schemes, depending on configuration:

- `JWT` for local Sparta authentication. It is emitted only when `Authentication:Local:Enabled` is true.
- `Entra` for Microsoft Entra delegated authentication. Scalar preconfigures authorization code flow, the requested scope, and PKCE SHA-256.

`Authentication:Entra:SwaggerClientId` retains its existing configuration name for compatibility, but it represents the browser/SPA client ID used by Scalar. Scalar must never receive a client secret, API key, bearer token, or password from server configuration. Browser-based clients cannot keep secrets.

Authentication persistence is intentionally not enabled. Access tokens should not be retained in browser local storage by the API reference.

## Production publishing policy

Production does not currently expose Scalar or the OpenAPI JSON. Keep that secure default until a deliberate publishing model is implemented.

The recommended model is two independently controlled documents:

| Document | Suggested access | Content |
|---|---|---|
| `public-v1` | Anonymous or developer-portal access | Explicitly supported consumer endpoints only |
| `internal-v1` | Entra policy such as `ApiDocs.Reader` | XAF/OData, metadata, audit, permissions and operational endpoints |

Use an allowlist to build `public-v1`. Do not publish the complete generated XAF/OData surface and then rely on hiding selected endpoints. The Scalar page and its backing OpenAPI JSON must have the same access policy; protecting only `/scalar` still leaves the API description exposed.

Before enabling production documentation:

1. Add an explicit `ApiDocumentation:Enabled` setting instead of inferring publication solely from the environment name.
2. Generate separate public and internal documents with an allowlist for the public document.
3. Apply the same authorization policy to the Scalar route and its OpenAPI JSON route.
4. Register an exact HTTPS Scalar redirect URI in the Entra SPA registration.
5. Continue using authorization code with PKCE SHA-256; do not configure a client secret or implicit grant.
6. Keep persistent authentication disabled.
7. Disable external fonts and configure a per-request Content Security Policy nonce.
8. Restrict CORS to the exact documentation origin when the reference is hosted separately from the API.
9. Redact authorization headers, cookies, OAuth codes and tokens from application, proxy and telemetry logs.
10. Apply gateway/WAF controls and rate limits, including OData query-complexity limits.

OpenAPI security definitions improve the API reference experience but do not enforce API access. Authentication, XAF object/member permissions, input validation, throttling and resource-level authorization remain mandatory on every endpoint.

## Verification

Build the API:

```powershell
dotnet build src/Sparta.Api/Sparta.Api.csproj --nologo
```

With the API running in Development, verify the routes and generated groups:

```powershell
$spec = Invoke-RestMethod http://localhost:5180/swagger/v1/swagger.json
$spec.'x-tagGroups' | ConvertTo-Json -Depth 5
(Invoke-WebRequest http://localhost:5180/scalar/ -UseBasicParsing).StatusCode
```

Expected results:

- The build succeeds without warnings or errors.
- Scalar returns HTTP 200.
- The OpenAPI document contains `Platform`, `Sales`, and `Inventory` in `x-tagGroups`.
- Sales and Inventory custom operations no longer share the generic `Business` tag.

## Relevant source files

- `src/Sparta.Api/Sparta.Api.csproj`
- `src/Sparta.Api/Startup.cs`
- `src/Sparta.Api/API/Documentation/ScalarNavigationFilters.cs`
- `src/Sparta.Api/appsettings.json`
- `docs/ENTRA.md`

## References

- [Scalar ASP.NET Core integration](https://github.com/scalar/scalar/blob/main/documentation/integrations/aspnetcore/integration.md)
- [Scalar OpenAPI extensions](https://github.com/scalar/scalar/blob/main/documentation/openapi.md)
- [Microsoft identity platform authorization code flow with PKCE](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow)
