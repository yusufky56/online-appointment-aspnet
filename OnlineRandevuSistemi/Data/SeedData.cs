using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace OnlineRandevuSistemi.Data
{
    public static class SeedData
    {
        private static readonly string[] Roles = { "Admin", "User" };

        /// <summary>
        /// Applies migrations, creates the roles and, if configured, an admin account
        /// (Seed:AdminEmail / Seed:AdminPassword).
        /// </summary>
        public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
        {
            using var scope = services.CreateScope();
            var provider = scope.ServiceProvider;

            var db = provider.GetRequiredService<ApplicationDbContext>();
            if (db.Database.IsRelational())
            {
                await db.Database.MigrateAsync();
            }

            var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in Roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            var email = config["Seed:AdminEmail"];
            var password = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            var userManager = provider.GetRequiredService<UserManager<IdentityUser>>();
            var admin = await userManager.FindByEmailAsync(email);
            if (admin == null)
            {
                admin = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                var result = await userManager.CreateAsync(admin, password);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Admin user could not be created: " + string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }

            if (!await userManager.IsInRoleAsync(admin, "Admin"))
            {
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }
    }
}
