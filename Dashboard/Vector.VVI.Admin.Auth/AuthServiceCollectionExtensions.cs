using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Vector.VVI.Admin.Auth.Services;

namespace Vector.VVI.Admin.Auth;

/// <summary>Registers the auth services and role policies used across the dashboard.</summary>
public static class AuthServiceCollectionExtensions
{
    public const string AdminOnly = "AdminOnly";
    public const string Authenticated = "Authenticated";

    public static IServiceCollection AddVviAuth(this IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<IEmailSender, DevStubEmailSender>();

        services.AddAuthorizationCore(options =>
        {
            options.AddPolicy(AdminOnly, p => p.RequireRole("admin"));
            options.AddPolicy(Authenticated, p => p.RequireAuthenticatedUser());
        });

        return services;
    }
}
