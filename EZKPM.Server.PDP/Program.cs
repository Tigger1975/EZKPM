using EZKPM.Server.PDP.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();

// Datenbank-Provider auslesen
var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "SQLite";
var connectionString = builder.Configuration.GetValue<string>("Database:ConnectionString") ?? "Data Source=ezkpm_vault.db";

builder.Services.AddDbContext<EzkpmDbContext>(options =>
{
    if (dbProvider.Equals("SqlServer", System.StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

// P2P Sync Engine als BackgroundService registrieren
builder.Services.AddSingleton<EZKPM.Server.PDP.Services.P2PSyncTrigger>();
builder.Services.AddHostedService<EZKPM.Server.PDP.Services.GossipSyncEngine>();

// JWT Konfiguration für Decentralized Auth
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Warnung: In Produktion sollte dieser Key aus dem Key Vault oder der Config kommen!
        var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? "EZKPM_Fallback_Secret_Key_32_Bytes_Long_Minimum");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "EZKPM_Server",
            ValidAudience = "EZKPM_Client",
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Headers.TryGetValue("X-Vault-Token", out var token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                try { System.IO.File.AppendAllText("C:\\inetpub\\EZKPM\\jwt_debug.txt", $"[{DateTime.UtcNow}] Auth Failed: {context.Exception.Message}\n"); } catch {}
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                try { System.IO.File.AppendAllText("C:\\inetpub\\EZKPM\\jwt_debug.txt", $"[{DateTime.UtcNow}] Challenge: {context.Error}, {context.ErrorDescription}\n"); } catch {}
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<EzkpmDbContext>();
    })
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("/connect/authorize")
               .SetTokenEndpointUris("/connect/token");

        options.AllowAuthorizationCodeFlow();
        options.RequireProofKeyForCodeExchange();

        // DEV ONLY: Do not use in production
        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddSingleton<EZKPM.Server.PDP.Services.PendingAuthStore>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Important for Apache Reverse Proxy / Let's Encrypt in DMZ
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Cleared to allow forwarding from any network (DMZ Apache config handles security)
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHostedService<EZKPM.Server.PDP.Services.ServerVulnerabilityScannerService>();
builder.Services.AddHostedService<EZKPM.Server.PDP.Services.LogRotationService>();

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Datenbank erstellen, falls nicht vorhanden
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EzkpmDbContext>();
    db.Database.Migrate();

    // Convert plain text SIDs to HMAC-SHA256 blinded indices for Zero-Knowledge
    var aclsToConvert = db.AssetAcls.Where(a => a.HashedSid != null && !a.HashedSid.EndsWith("=")).ToList();
    foreach (var acl in aclsToConvert)
    {
        db.AssetAcls.Remove(acl);
        db.AssetAcls.Add(new AssetAcl 
        {
            AssetId = acl.AssetId,
            HashedSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(acl.HashedSid),
            PermissionLevel = acl.PermissionLevel,
            EncryptedKeyShare = acl.EncryptedKeyShare
        });
    }

    var logsToConvert = db.AuditLogs.Where(l => l.ActorHashedSid != null && !l.ActorHashedSid.EndsWith("=")).ToList();
    foreach (var log in logsToConvert)
        log.ActorHashedSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(log.ActorHashedSid);

    var sharesToConvert = db.VaultRecoveryShares.Where(s => s.AdminHashedSid != null && !s.AdminHashedSid.EndsWith("=")).ToList();
    foreach (var share in sharesToConvert)
        share.AdminHashedSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(share.AdminHashedSid);

    var requestsToConvert = db.VaultRecoveryRequests.Where(r => r.TargetHashedSid != null && !r.TargetHashedSid.EndsWith("=")).ToList();
    foreach (var req in requestsToConvert)
        req.TargetHashedSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(req.TargetHashedSid);

    var profilesToConvert = db.UserProfiles.Where(p => p.HashedSid != null && !p.HashedSid.EndsWith("=")).ToList();
    foreach (var profile in profilesToConvert)
    {
        db.UserProfiles.Remove(profile);
        db.UserProfiles.Add(new UserProfile 
        {
            HashedSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(profile.HashedSid),
            EncryptedMasterKeyBackup = profile.EncryptedMasterKeyBackup
        });
    }

    if (aclsToConvert.Count > 0 || logsToConvert.Count > 0 || sharesToConvert.Count > 0 || requestsToConvert.Count > 0 || profilesToConvert.Count > 0)
    {
        db.SaveChanges();
    }
}

app.UseHttpsRedirection();

// Für den lokalen Test bypassen wir Auth Middleware, 
// wir setzen stattdessen in den Controllern eine Dummy-SID, falls keine Auth da ist.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<EZKPM.Server.PDP.Hubs.ClientSyncHub>("/hubs/sync");

app.Run();


