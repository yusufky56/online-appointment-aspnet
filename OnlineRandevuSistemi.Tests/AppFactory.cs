using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineRandevuSistemi.Data;

namespace OnlineRandevuSistemi.Tests;

/// <summary>
/// Runs the real app against an in-memory SQLite database. Requests are
/// authenticated as the user named in the X-Test-User header.
/// </summary>
public class AppFactory : WebApplicationFactory<Program>
{
    public const string Alice = "user-alice";
    public const string Bob = "user-bob";
    public const string Admin = "user-admin";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AppFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));

            // Authenticate with the test scheme; challenges still go to the Identity
            // cookie scheme, so anonymous users are redirected to the login page as usual.
            services.AddAuthentication(o => o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public async Task SeedUsersAsync()
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        foreach (var id in new[] { Alice, Bob, Admin })
        {
            if (await users.FindByIdAsync(id) == null)
            {
                await users.CreateAsync(new IdentityUser { Id = id, UserName = $"{id}@test.com", Email = $"{id}@test.com" });
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var userId) || string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId!),
            new(ClaimTypes.Name, $"{userId}@test.com"),
        };
        if (userId == AppFactory.Admin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            services.Remove(d);
        }
    }
}
