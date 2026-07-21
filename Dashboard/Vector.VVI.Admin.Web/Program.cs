using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;
using Vector.VVI.Admin.Auth;
using Vector.VVI.Admin.Data;
using Vector.VVI.Admin.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog (daily rolling files; see plan section 11) ----
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        @"C:\InMotion\OTR_Admin\logs\vvi-admin-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31)
    .CreateLogger();
builder.Host.UseSerilog();

// ---- Database (VendorAPI_FK) ----
var connectionString = builder.Configuration.GetConnectionString("VendorApiFk")
    ?? throw new InvalidOperationException("Connection string 'VendorApiFk' is not configured.");
builder.Services.AddDbContext<VviDbContext>(options => options.UseSqlServer(connectionString));

// ---- Authentication (cookie, 8h sliding) + auth services/policies ----
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "vvi_admin_auth";
    });
builder.Services.AddVviAuth();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// ---- MudBlazor + Blazor (interactive server) ----
builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

try
{
    Log.Information("Starting VVI Admin dashboard");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "VVI Admin dashboard terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
