using Microsoft.AspNetCore.Mvc;
using robeem.HR;
using robeem.Infrastructure;
using robeem.Infrastructure.Persistence;
using robeem.Web.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddRobeemInfrastructure(builder.Configuration);
builder.Services.AddRobeemHr();
builder.Services.AddScoped<TenantResolutionMiddleware>();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:InitializeOnStartup"))
{
    await app.Services.GetRequiredService<TenantDatabaseInitializer>().InitializeAllAsync(app.Lifetime.ApplicationStopping);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// When authentication is added, UseAuthentication() must come before this line so the
// tenant can be read from the user's tenant_id claim.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Employees}/{action=Index}/{id?}")
    .WithStaticAssets();

await app.RunAsync();
