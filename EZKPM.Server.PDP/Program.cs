using EZKPM.Server.PDP.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Für Phase 2 Test: In-Memory DB statt PostgreSQL nutzen, um direkt loslegen zu können
builder.Services.AddDbContext<EzkpmDbContext>(options =>
    options.UseInMemoryDatabase("EzkpmTestDb"));

// TODO: In einer echten Umgebung OIDC Authentication hinzufügen
// builder.Services.AddAuthentication(...)

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Für den lokalen Test bypassen wir Auth Middleware, 
// wir setzen stattdessen in den Controllern eine Dummy-SID, falls keine Auth da ist.
app.UseAuthorization();

app.MapControllers();

app.Run();
