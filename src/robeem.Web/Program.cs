using Microsoft.AspNetCore.Mvc;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<TenantSelectionRequiredFilter>();
    options.Filters.Add<ApiAccessDeniedFilter>();
    options.Filters.Add<ActorAccessFilter>();
});
builder.Services.AddRobeemApiClients();
builder.Services.AddSingleton<StandInCache>();
builder.Services.AddScoped<ICurrentActor, CurrentActor>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

await app.RunAsync();
