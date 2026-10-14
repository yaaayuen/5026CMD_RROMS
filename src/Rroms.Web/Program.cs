// =============================================================================
// Three Grills - Restaurant Reservation and Order Management System (RROMS)
// Application entry point: registers services, configures security and starts the web server.
//
// Reading order:
//   1. Services   - what the application can use (database, Identity, Razor Pages, ...)
//   2. Pipeline   - what happens to every HTTP request, in order
//   3. Start-up   - database preparation, then run
// =============================================================================

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Rroms.Web.Constants;
using Rroms.Web.Security;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Options;
using Rroms.Web.Services;
using Rroms.Web.Hubs;
using Rroms.Web.Services.Email;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// 1. SERVICES (dependency injection container)
// -----------------------------------------------------------------------------

// ----- Database -----
// The connection string comes from appsettings.json ("DefaultConnection") and points to
// SQL Server LocalDB, which can be opened in SQL Server Management Studio as (localdb)\MSSQLLocalDB.
string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found in configuration.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // EnableRetryOnFailure retries short network glitches instead of failing the request.
    options.UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure());
});

// ----- Identity (accounts, passwords, roles) -----
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Password policy. Identity enforces it on registration and password change.
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        // Lockout slows down password-guessing attacks:
        // after 5 wrong attempts the account is locked for 15 minutes.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;

        // One account per email address.
        options.User.RequireUniqueEmail = true;

        // Email confirmation is not required for this coursework system.
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>() // Store users and roles in our SQL Server database.
    .AddSignInManager<ApplicationSignInManager>()          // Our custom sign-in manager that blocks deactivated accounts.
    .AddDefaultTokenProviders();                       // Tokens for password reset and similar features.

// ----- Authentication cookie -----
builder.Services.ConfigureApplicationCookie(options =>
{
    // Where users are sent when they must sign in, or lack the required role.
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";

    // HttpOnly: JavaScript cannot read the cookie, which blocks cookie theft through XSS.
    options.Cookie.HttpOnly = true;
    // Always: the cookie is only sent over HTTPS.
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    // Lax: the cookie is not sent with most cross-site requests, which helps against CSRF.
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Name = "ThreeGrills.Auth";

    // Sign the user out after 2 hours of inactivity; activity extends the session.
    options.ExpireTimeSpan = TimeSpan.FromHours(2);
    options.SlidingExpiration = true;
});

// Re-check every signed-in user's security stamp each minute (default is 30 minutes).
// When an administrator deactivates a user or changes their role (YY-5), the stamp changes,
// so that user's existing cookie stops working within a minute.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
});

// ----- Configuration objects (Options pattern) -----
// Each class is filled from its appsettings.json section and injected as IOptions<T>.
builder.Services.Configure<BootstrapAdminOptions>(
    builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
builder.Services.Configure<ReservationRulesOptions>(
    builder.Configuration.GetSection(ReservationRulesOptions.SectionName));
builder.Services.Configure<AuthRateLimitOptions>(
    builder.Configuration.GetSection(AuthRateLimitOptions.SectionName));
builder.Services.Configure<MenuImageOptions>(
    builder.Configuration.GetSection(MenuImageOptions.SectionName));
builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection(SmtpOptions.SectionName));

// ----- Rate limiting for the login and registration forms (YY-2) -----
// Each IP address gets its own fixed window of requests. Requests over the limit receive
// HTTP 429 "Too Many Requests" instead of reaching the page.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(AuthRateLimitOptions.PolicyName, httpContext =>
    {
        // Read the limits per request, so tests and deployments can change them in configuration.
        AuthRateLimitOptions limits = httpContext.RequestServices
            .GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;

        string clientAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientAddress, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limits.PermitLimit,
            Window = TimeSpan.FromSeconds(limits.WindowSeconds),
            QueueLimit = 0 // Reject immediately rather than making the request wait.
        });
    });
});

// ----- Application services -----
// Scoped = one instance per HTTP request, the same lifetime as the DbContext they use.
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<IAccountService, AccountService>(); //YY-2
builder.Services.AddScoped<IProfileService, ProfileService>(); //YY-4
builder.Services.AddScoped<IUserAdministrationService, UserAdministrationService>();  //YY-5
builder.Services.AddScoped<IAccountSecurityService, AccountSecurityService>(); //YY-7

// Restaurant setup: profile, hours, closures, areas, tables, menu categories (WL-1).
builder.Services.AddScoped<IRestaurantService, RestaurantService>();
// Booking time rules: visit length, slot grid and overlap check, all from ReservationRules (WL-2).
builder.Services.AddScoped<ReservationTimeRules>();
builder.Services.AddScoped<IReservationService, ReservationService>();

// Menu items: dishes, prices, availability and offers (YJ-1).
builder.Services.AddScoped<IMenuService, MenuService>();

// Dish photos: validation, resizing and storage (YJ-1).
builder.Services.AddScoped<IMenuImageStorage, MenuImageStorage>();

// Alex (Sprint 4)
builder.Services.AddScoped<IOrderProcessingService, OrderProcessingService>();
builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();

// ----- Notifications (YY-6) -----
// One class serves both interfaces: modules publish through INotificationService,
// the inbox page and bell read through INotificationInbox.
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<INotificationInbox, NotificationService>();
builder.Services.AddScoped<INotificationPusher, SignalRNotificationPusher>();
builder.Services.AddSignalR();

// Email: pages add to an in-memory queue (singleton, shared by all requests);
// a background service sends from the queue through Gmail.
builder.Services.AddSingleton<IEmailQueue, EmailQueue>();
builder.Services.AddSingleton<IEmailDelivery, SmtpEmailDelivery>();
builder.Services.AddHostedService<EmailBackgroundService>();

// ----- Authorization policies (YY-3) -----
// Each policy names a rule once; folders and pages refer to it by name.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PolicyNames.AdministratorOnly, policy =>
        policy.RequireRole(RoleNames.Administrator));

    // RequireRole with several roles means "any one of these".
    options.AddPolicy(PolicyNames.StaffOrAdministrator, policy =>
        policy.RequireRole(RoleNames.Staff, RoleNames.Administrator));

    options.AddPolicy(PolicyNames.CustomerOnly, policy =>
        policy.RequireRole(RoleNames.Customer));
});

// ----- Razor Pages -----
builder.Services.AddRazorPages(options =>
{
    // Protect whole folders, so a new page added later is secure by default.
    // Nobody can forget an [Authorize] attribute on an individual page.
    options.Conventions.AuthorizeAreaFolder("Admin", "/", PolicyNames.AdministratorOnly);
    options.Conventions.AuthorizeAreaFolder("Staff", "/", PolicyNames.StaffOrAdministrator);
    options.Conventions.AuthorizeAreaFolder("Customer", "/", PolicyNames.CustomerOnly);
    // Pages any signed-in user may use, whatever their role.
    options.Conventions.AuthorizePage("/Account/Profile");
    options.Conventions.AuthorizePage("/Account/ChangePassword");
    options.Conventions.AuthorizePage("/Account/TwoStep");
    options.Conventions.AuthorizeFolder("/Notifications");
});

// -----------------------------------------------------------------------------
// 2. HTTP REQUEST PIPELINE (middleware runs in this exact order)
// -----------------------------------------------------------------------------
WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Detailed error pages are only shown to developers, never to real users.
    app.UseDeveloperExceptionPage();
}
else
{
    // Friendly error page for users; details go to the log instead.
    app.UseExceptionHandler("/Error");

    // HSTS tells browsers to only ever use HTTPS for this site.
    app.UseHsts();
}

app.UseHttpsRedirection();   // Redirect http:// to https://.
app.UseStaticFiles();        // Serve CSS, JavaScript and images from wwwroot.
app.UseRouting();            // Work out which page the URL refers to.
app.UseRateLimiter();       // Limit requests to the login and registration pages.
app.UseAuthentication();     // Read the auth cookie and identify the user.
app.UseAuthorization();      // Check the user is allowed to see that page.

app.MapRazorPages();
app.MapHub<NotificationHub>(NotificationHub.Path); // Live notifications (YY-6).

// -----------------------------------------------------------------------------
// 3. START-UP: prepare the database, then start listening for requests
// -----------------------------------------------------------------------------
await DatabaseInitializer.InitializeAsync(app.Services, app.Environment, app.Configuration);

app.Run();

/// <summary>
/// Makes the compiler-generated Program class public so the test project can start the app
/// in memory with WebApplicationFactory&lt;Program&gt;.
/// </summary>
public partial class Program
{
}