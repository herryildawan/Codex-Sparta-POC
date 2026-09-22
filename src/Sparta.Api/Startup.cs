using System.Text;
using System.IO.Compression;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ApplicationBuilder;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.Security.Authentication.ClientServer;
using DevExpress.ExpressApp.WebApi.Services;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.Identity.Web;
using System.IdentityModel.Tokens.Jwt;
using Sparta.Audit;
using Sparta.Security;
using Sparta.Modules.Sales;
using Sparta.Modules.Inventory;
using Sparta.WebApi.JWT;
using Sparta.Security.BusinessObject;
using Sparta.Modules.Sales.BusinessObject;
using Sparta.Modules.Inventory.BusinessObjects;
using Sparta.Api.Services;
using Sparta.WebApi.Documentation;
using Scalar.AspNetCore;

namespace Sparta.WebApi;
public class Startup(IConfiguration configuration, IWebHostEnvironment hostEnvironment)
{
    public IConfiguration Configuration { get; } = configuration;
    public IWebHostEnvironment HostEnvironment { get; } = hostEnvironment;

    public void ConfigureServices(IServiceCollection services)
    {
        var automaticallyUpdateSchema = HostEnvironment.IsDevelopment();
        var entraEnabled = Configuration.GetValue<bool>("Authentication:Entra:Enabled");
        var localEnabled = Configuration.GetValue("Authentication:Local:Enabled", true);

        if (!entraEnabled && !localEnabled) throw new InvalidOperationException("Enable at least one authentication provider.");
        if (entraEnabled && (!Guid.TryParse(Configuration["Authentication:Entra:TenantId"], out _) ||
            !Guid.TryParse(Configuration["Authentication:Entra:ClientId"], out _)))
            throw new InvalidOperationException("Entra requires a specific tenant GUID and API client GUID.");

        services.AddScoped<Sparta.SharedKernel.IProductCatalog, ProductCatalog>();
        services.AddScoped<IAuthenticationTokenProvider, JwtTokenProviderService>();
        
        services.AddXafWebApi(builder =>
        {
            builder.ConfigureOptions(options =>
            {
                options.BusinessObject<Customer>();
                options.BusinessObject<SalesOrder>();
                options.BusinessObject<SalesOrderLine>();
                options.BusinessObject<Product>();
                options.BusinessObject<Warehouse>();
                options.BusinessObject<StockMovement>();
            });
            builder.Modules.AddValidation().Add<SpartaModule>();

            builder.ObjectSpaceProviders
                // configure the SecurityDbContext with secured EF Core
                .AddSecuredEFCore(o => { o.PreFetchReferenceProperties(); o.SchemaUpdateOptions.DisableUpdateSchema = !automaticallyUpdateSchema; })
                .WithDbContext<SecurityDbContext>((sp, o) => ConfigureDb(o, "Security"))

                // configure the SalesDbContext with auditing, using the AuditDbContext for audit logs
                .AddSecuredEFCore(o => { o.PreFetchReferenceProperties(); o.SchemaUpdateOptions.DisableUpdateSchema = !automaticallyUpdateSchema; })
                .WithAuditedDbContext(contexts => contexts.Configure<SalesDbContext, AuditDbContext>
                (
                    (sp, o) => ConfigureDb(o, "Sales"), (sp, o) => { ConfigureDb(o, "Audit"); o.AddInterceptors(sp.GetServices<IAuditSaveInterceptor>()); },
                    o => o.AuditFilterDataProviderType = typeof(SafeAuditFilter))
                )
                
                // configure the InventoryDbContext with auditing, using the AuditDbContext for audit logs
                .AddSecuredEFCore(o => { o.PreFetchReferenceProperties(); o.SchemaUpdateOptions.DisableUpdateSchema = !automaticallyUpdateSchema; })
                .WithAuditedDbContext(contexts => contexts.Configure<InventoryDbContext, AuditDbContext>
                (
                    (sp, o) => ConfigureDb(o, "Inventory"), (sp, o) => { ConfigureDb(o, "Audit"); o.AddInterceptors(sp.GetServices<IAuditSaveInterceptor>()); },
                    o => o.AuditFilterDataProviderType = typeof(SafeAuditFilter))
                )
                
                // Add a non-persistent object space provider for transient objects
                .AddNonPersistent();

            builder.Security.UseIntegratedMode(options =>
            {
                options.Lockout.Enabled = true;
                options.RoleType = typeof(PermissionPolicyRole);
                options.UserType = typeof(ApplicationUser);
                options.UserLoginInfoType = typeof(ApplicationUserLoginInfo);
                options.Events.OnSecurityStrategyCreated += strategy =>
                {
                    ((SecurityStrategy)strategy).PermissionsReloadMode = PermissionsReloadMode.NoCache;
                    ((SecurityStrategy)strategy).AssociationPermissionsMode = AssociationPermissionsMode.Manual;
                };
            })
            .AddPasswordAuthentication()
            .AddAuthenticationProvider<EntraXafAuthenticationProvider>();

            builder.AddBuildStep(application =>
            {
                application.ApplicationName = "Sparta";
                application.CheckCompatibilityType = CheckCompatibilityType.DatabaseSchema;
                application.DatabaseUpdateMode = automaticallyUpdateSchema
                    ? DatabaseUpdateMode.UpdateDatabaseAlways
                    : DatabaseUpdateMode.Never;

                if (automaticallyUpdateSchema)
                {
                    application.DatabaseVersionMismatch += (_, e) =>
                    {
                        e.Updater.Update();
                        e.Handled = true;
                    };
                }
            });
        }, Configuration);

        services.AddScoped<IDataService, ValidatedDataService>();
        
        services.AddControllers().AddOData((options, sp) => options
            .AddRouteComponents("api/odata", new EdmModelBuilder(sp).GetEdmModel(), Microsoft.OData.ODataVersion.V401,
                routes => routes.ConfigureXafWebApiServices()).EnableQueryFeatures(100));

        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = Configuration.GetValue("ResponseCompression:EnableForHttps", true);
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        
        const string routingScheme = "SpartaBearer";
        var authentication = services.AddAuthentication(routingScheme).AddPolicyScheme(routingScheme, null, options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                if (!localEnabled) return EntraAuthentication.Scheme;
                if (!entraEnabled) return JwtBearerDefaults.AuthenticationScheme;
                
                var header = context.Request.Headers.Authorization.ToString();
                if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = header[7..].Trim();
                    var reader = new JwtSecurityTokenHandler();
                    // Unvalidated issuer selects a validator only. Both validators still verify the token.
                    if (reader.CanReadToken(token) && reader.ReadJwtToken(token).Issuer == "Sparta")
                        return JwtBearerDefaults.AuthenticationScheme;
                }
                return EntraAuthentication.Scheme;
            };
        });

        if (localEnabled) authentication.AddJwtBearer(options =>
        {
            var key = Configuration["Authentication:Jwt:IssuerSigningKey"]
                ?? throw new InvalidOperationException("Configure the JWT key in User Secrets or environment variables.");
            
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidIssuer = "Sparta",
                ValidAudience = "Sparta.Api",
                ClockSkew = TimeSpan.FromSeconds(30),
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                AuthenticationType = JwtBearerDefaults.AuthenticationScheme
            };
        });

        if (entraEnabled)
        {
            authentication.AddMicrosoftIdentityWebApi(Configuration.GetSection("Authentication:Entra"), jwtBearerScheme: EntraAuthentication.Scheme);
            services.PostConfigure<JwtBearerOptions>(EntraAuthentication.Scheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters.AuthenticationType = EntraAuthentication.Scheme;
                options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256];
                var expectedIssuer = $"https://login.microsoftonline.com/{Configuration["Authentication:Entra:TenantId"]}/v2.0";
                options.TokenValidationParameters.IssuerValidator = (issuer, _, _) =>
                    issuer == expectedIssuer ? issuer : throw new SecurityTokenInvalidIssuerException("Unexpected Entra issuer.");
                var previous = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async context =>
                {
                    if (previous != null) await previous(context);
                    if (context.Result?.Failure == null) await EntraAuthentication.ValidateToken(context, Configuration);
                };
            });
        }

        services.AddAuthorization(options => options.DefaultPolicy = new AuthorizationPolicyBuilder(routingScheme)
            .RequireAuthenticatedUser().RequireXafAuthentication().Build());
        
        services.AddSwaggerGen(c =>
        {
            c.EnableAnnotations();
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Sparta Architecture POC", Version = "v1" });
            c.OperationFilter<ScalarOperationTagsFilter>();
            c.DocumentFilter<ScalarTagGroupsFilter>();
            if (localEnabled)
            {
                c.AddSecurityDefinition("JWT", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "JWT" } }] = []
                });
            }
            if (entraEnabled)
            {
                var authority = $"https://login.microsoftonline.com/{Configuration["Authentication:Entra:TenantId"]}";
                var scope = $"api://{Configuration["Authentication:Entra:ClientId"]}/{Configuration["Authentication:Entra:RequiredScope"] ?? "access_as_user"}";
                c.AddSecurityDefinition("Entra", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri($"{authority}/oauth2/v2.0/authorize"),
                            TokenUrl = new Uri($"{authority}/oauth2/v2.0/token"),
                            Scopes = new Dictionary<string, string> { [scope] = "Access Sparta as the signed-in user" }
                        }
                    }
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Entra" } }] = [scope]
                });
            }
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        
        foreach (var name in new[] { "Security", "Sales", "Inventory", "Audit" }) services.AddHealthChecks().Add(
            new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration(name,
                sp => new Sparta.WebApi.Telemetry.DatabaseHealthCheck(sp.GetRequiredService<IConfiguration>().GetConnectionString(name)!), null, null));
    }
    private void ConfigureDb(DbContextOptionsBuilder options, string name)
    {
        options.UseConnectionString
        (
            Configuration.GetConnectionString(name) ?? throw new InvalidOperationException($"Missing connection string: {name}")
        );

#if DEBUG
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
#endif

        options.LogTo(Console.WriteLine, [DbLoggerCategory.Database.Command.Name], LogLevel.Information);
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (Configuration.GetValue("ResponseCompression:Enabled", true))
            app.UseResponseCompression();

        app.UseExceptionHandler();
        if (env.IsDevelopment())
        {
            app.UseSwagger();
        }
        else { app.UseHsts(); app.UseHttpsRedirection(); }
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapXafEndpoints();

            if (env.IsDevelopment())
            {
                endpoints.MapGet("/", () => Results.Redirect("/scalar/"));
                endpoints.MapScalarApiReference(options =>
                {
                    options.WithTitle("Sparta Architecture POC")
                        .WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json")
                        .DisableAgent();

                    if (Configuration.GetValue<bool>("Authentication:Entra:Enabled"))
                    {
                        var scope = $"api://{Configuration["Authentication:Entra:ClientId"]}/{Configuration["Authentication:Entra:RequiredScope"] ?? "access_as_user"}";
                        options.AddPreferredSecuritySchemes("Entra")
                            .AddAuthorizationCodeFlow("Entra", flow =>
                            {
                                flow.ClientId = Configuration["Authentication:Entra:SwaggerClientId"];
                                flow.Pkce = Pkce.Sha256;
                                flow.SelectedScopes = [scope];
                            });
                    }
                });
            }
        });
    }
}

