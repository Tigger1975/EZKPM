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

// Für Phase 2 Test: SQLite statt In-Memory, um Daten dauerhaft zu speichern
builder.Services.AddDbContext<EzkpmDbContext>(options =>
    options.UseSqlite("Data Source=ezkpm_vault.db"));

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
}

app.UseHttpsRedirection();

// Für den lokalen Test bypassen wir Auth Middleware, 
// wir setzen stattdessen in den Controllern eine Dummy-SID, falls keine Auth da ist.
app.UseAuthorization();

app.MapControllers();
app.MapHub<EZKPM.Server.PDP.Hubs.ClientSyncHub>("/hubs/sync");

app.Run();
