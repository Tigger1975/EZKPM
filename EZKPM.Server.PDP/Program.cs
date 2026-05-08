using EZKPM.Server.PDP.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

// TODO: In einer echten Umgebung OIDC Authentication hinzufügen
// builder.Services.AddAuthentication(...)

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
    var aclsToConvert = db.AssetAcls.Where(a => a.AdSid != null && !a.AdSid.EndsWith("=")).ToList();
    foreach (var acl in aclsToConvert)
    {
        db.AssetAcls.Remove(acl);
        db.AssetAcls.Add(new AssetAcl 
        {
            AssetId = acl.AssetId,
            AdSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(acl.AdSid),
            PermissionLevel = acl.PermissionLevel,
            EncryptedKeyShare = acl.EncryptedKeyShare
        });
    }

    var logsToConvert = db.AuditLogs.Where(l => l.ActorSid != null && !l.ActorSid.EndsWith("=")).ToList();
    foreach (var log in logsToConvert)
        log.ActorSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(log.ActorSid);

    var sharesToConvert = db.VaultRecoveryShares.Where(s => s.AdminSid != null && !s.AdminSid.EndsWith("=")).ToList();
    foreach (var share in sharesToConvert)
        share.AdminSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(share.AdminSid);

    var requestsToConvert = db.VaultRecoveryRequests.Where(r => r.AdSid != null && !r.AdSid.EndsWith("=")).ToList();
    foreach (var req in requestsToConvert)
        req.AdSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(req.AdSid);

    var profilesToConvert = db.UserProfiles.Where(p => p.AdSid != null && !p.AdSid.EndsWith("=")).ToList();
    foreach (var profile in profilesToConvert)
    {
        db.UserProfiles.Remove(profile);
        db.UserProfiles.Add(new UserProfile 
        {
            AdSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(profile.AdSid),
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
app.UseAuthorization();

app.MapControllers();
app.MapHub<EZKPM.Server.PDP.Hubs.ClientSyncHub>("/hubs/sync");

app.Run();
