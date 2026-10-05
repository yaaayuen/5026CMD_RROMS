using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Options;

namespace Rroms.Web.Data;

/// <summary>
/// Creates the data the system cannot run without: the three roles and the first administrator.
/// </summary>

public class DatabaseSeeder
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BootstrapAdminOptions _adminOptions;
    private readonly ILogger<DatabaseSeeder> _logger;

    /// <summary>
    /// All dependencies are supplied by dependency injection.
    /// </summary>
    public DatabaseSeeder(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IOptions<BootstrapAdminOptions> adminOptions,
        ILogger<DatabaseSeeder> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _adminOptions = adminOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs every seeding step in order. Roles must exist before a user can be added to one.
    /// </summary>
    public async Task SeedAsync()
    {
        await SeedRolesAsync();
        await SeedAdministratorAsync();
    }

    /// <summary>
    /// Makes sure Administrator, Staff and Customer roles exist.
    /// </summary>
    private async Task SeedRolesAsync()
    {
        foreach (string roleName in RoleNames.All)
        {
            bool exists = await _roleManager.RoleExistsAsync(roleName);
            if (exists)
            {
                continue;
            }

            IdentityResult result = await _roleManager.CreateAsync(new IdentityRole(roleName));
            EnsureSucceeded(result, $"create role '{roleName}'");
            _logger.LogInformation("Created role {RoleName}.", roleName);
        }
    }

    /// <summary>
    /// Creates the first administrator from configuration, or makes sure the existing
    /// account still has the Administrator role.
    /// </summary>
    private async Task SeedAdministratorAsync()
    {
        string email = _adminOptions.Email.Trim();
        string password = _adminOptions.Password;

        // No hard-coded fallback password: if nothing is configured we skip with a warning,
        // because a default password in source code would be visible to anyone on GitHub.
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "No bootstrap administrator configured. Set BootstrapAdmin:Email and BootstrapAdmin:Password with user-secrets.");
            return;
        }

        ApplicationUser? existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser != null)
        {
            // The account exists (e.g. from a previous run). Only make sure it is still an administrator.
            bool isAdmin = await _userManager.IsInRoleAsync(existingUser, RoleNames.Administrator);
            if (!isAdmin)
            {
                IdentityResult addRoleResult = await _userManager.AddToRoleAsync(existingUser, RoleNames.Administrator);
                EnsureSucceeded(addRoleResult, "restore the Administrator role on the bootstrap account");
            }

            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            // The address comes from a trusted configuration value, so no confirmation email is needed.
            EmailConfirmed = true,
            FullName = "System Administrator",
            IsActive = true
        };

        // CreateAsync hashes the password (PBKDF2 with a random salt) before saving it.
        // It also validates it against the password policy configured in Program.cs.
        IdentityResult createResult = await _userManager.CreateAsync(admin, password);
        EnsureSucceeded(createResult, "create the bootstrap administrator");

        IdentityResult roleResult = await _userManager.AddToRoleAsync(admin, RoleNames.Administrator);
        EnsureSucceeded(roleResult, "add the bootstrap administrator to the Administrator role");

        _logger.LogInformation("Created bootstrap administrator {Email}.", email);
    }

    /// <summary>
    /// Stops start-up with a clear message if an Identity operation failed,
    /// for example because the configured password is too weak.
    /// </summary>
    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (result.Succeeded)
        {
            return;
        }

        string reasons = string.Join(" ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Database seeding failed to {action}: {reasons}");
    }
}