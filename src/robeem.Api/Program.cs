using robeem.Api.Tenancy;
using robeem.Application;
using robeem.Infrastructure;
using robeem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddRobeemInfrastructure(builder.Configuration);
builder.Services.AddRobeemApplication();
builder.Services.AddScoped<TenantResolutionMiddleware>();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:InitializeOnStartup"))
{
    await app.Services.GetRequiredService<TenantDatabaseInitializer>().InitializeAllAsync(app.Lifetime.ApplicationStopping);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// When authentication is added, UseAuthentication() must come before this line so the
// tenant can be read from the user's tenant_id claim.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
