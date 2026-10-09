# AI context: Three Grills RROMS

> **How to use this file.** Attach it (together with `docs/shared-contracts.md`) at the start of every
> AI chat about this project, then paste your personal starter prompt. It gives the AI the decisions
> the team has already made, so it does not invent a different design.
> Never paste passwords, API keys, the Gmail app password or connection strings with passwords into an AI chat.

## 1. The project

- **Course:** 5026CMD group coursework. Web-Based Restaurant Reservation and Order Management System (RROMS).
- **Restaurant:** "Three Grills", a fictional Western grill café-bistro. Prices in RM; time zone Malaysia (UTC+8).
- **Roles:** Administrator, Restaurant Staff, Customer.
- **Required stack (from the brief, not negotiable):** C#, ASP.NET Core Web App (**Razor Pages**, not MVC, not Blazor, not Web API + SPA),
  HTML, CSS, **Bootstrap 5**, JavaScript, **SQL Server** managed in **SSMS**, **Visual Studio 2026**, .NET 10.
- **Deadlines:** code freeze **Fri 30 Oct 2026**; submission 6 Nov 2026; individual viva afterwards.

## 2. Team and sprints

| Sprint | Owner | Module | Dates |
|---|---|---|---|
| S1 | YY | Accounts, roles, profiles, notifications (inbox, SignalR, Gmail), base layout | 5–16 Oct |
| S2 | WL | Restaurant setup, reservations, occupancy analytics, dashboard | 7–21 Oct |
| S3 | YJ | Menu, ordering, recommendations, customer history, theme | 9–23 Oct |
| S4 | Alex | Announcements, staff order queue, live tracking, feedback, chatbot | 13–26 Oct |
| S5 | Darren | Billing and payments, staff daily reservations view, monitoring, reports, exports, deployment | 15–30 Oct |

Each member writes and commits **only their own module**. Shared files (Program.cs, ApplicationDbContext.cs) get small,
clearly commented additions in that member's section.

## 3. Architecture (already built in the skeleton)

```
Razor Pages (UI)  →  Services (business rules)  →  ApplicationDbContext (EF Core)  →  SQL Server
```

- Solution `Rroms.slnx`: `src/Rroms.Web` (the app) and `tests/Rroms.Tests` (xUnit + WebApplicationFactory + in-memory SQLite).
- Identity: `ApplicationUser : IdentityUser` (adds `FullName`, `DiningPreferences`, `IsActive`, `CreatedAtUtc`). User IDs are `string`.
- Role pages live in **Areas**: `Areas/Admin/Pages`, `Areas/Staff/Pages`, `Areas/Customer/Pages`. Public pages in `Pages/`.
- **PageModels never use `ApplicationDbContext` directly.** They call a service interface (`IXxxService`) registered as scoped in `Program.cs`.
- Business-rule failures return `ServiceResult` / `ServiceResult<T>` (`Common/ServiceResult.cs`); the PageModel copies `Errors` into `ModelState`.
  Exceptions are only for unexpected failures.
- Shared enums in `Contracts/`: `ReservationStatus`, `OrderStatus`, `PaymentStatus`, `PaymentMethod`, `NotificationType`. Stored as text.
- Notifications: inject `INotificationService` and call `NotifyUserAsync(...)` / `NotifyRoleAsync(...)` after saving (inbox + SignalR push + Gmail queue).
- Access: Admin/Staff/Customer areas are already protected by folder policies. Current user ID: `User.GetUserId()` (never from form input); someone else's record → 404.
- Booking rules: `IOptions<ReservationRulesOptions>` (120-min visit, 30-min slots, 90 days ahead, party 1–20). Never hard-code them.
- Time: store UTC (`...Utc` properties), display with `MalaysiaTime.FromUtc`, read form input with `MalaysiaTime.ToUtc`.
- Money: `decimal` only (stored as decimal(10,2) by convention). Never `double`.
- SQL Server `EnableRetryOnFailure` is on: any manual transaction must run inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`.
- Migrations: `Add-Migration S<sprint>_<Owner>_<Feature> -OutputDir Data/Migrations`, after pulling `main`.
- UI theme "Berry Bistro" in `wwwroot/css/site.css`: reuse its classes (`tg-card`, `hero`, `btn-primary`, `btn-gold`, `tg-icon`, `section-title`).
  No new colour palette; same design for all roles.

## 4. Code style (the team's agreed standard)

- Conventional C#: explicit constructors with `private readonly _camelCase` fields. **No primary constructors.**
- One class per file; file name = class name; braces on every `if`; `async`/`await` throughout.
- `/// <summary>` XML comments on every public class and method; inline comments explain **why**, not what.
- No magic numbers or strings: constants (`RoleNames`, `SiteInfo`) or Options classes.
- Validation on the server always (data annotations + service rules), client validation only as a convenience.
- Every form posts with the antiforgery token (Razor Pages adds it automatically; do not disable it).
- Readable over clever: no chained one-liners, no nested ternaries, no LINQ so dense it cannot be explained aloud.

## 5. Rules for the AI assistant (learning mode)

You are helping a student who must **explain and modify this code live in an individual viva**. Therefore:

1. **Explain before you write.** Describe the design for a task (entities, service methods, pages, tests) and wait for me to agree.
2. **One task at a time**, in small steps I can commit separately. Do not generate the whole module at once.
3. **Follow sections 3 and 4 exactly.** If something in them blocks you, say so instead of working around it.
4. **Do not change shared contracts** (`Contracts/`, `ServiceResult`, `INotificationService`, Options classes, other members' files).
   If you think a change is needed, tell me so I can ask the team.
5. **Tests with every feature**: unit tests for service rules, integration tests for page access where relevant.
6. After each piece of code, give me **3 viva-style questions** about it and check my answers.
7. If I paste an error, explain the cause before giving the fix.
8. Prefer techniques a second-year student can defend. If you use something advanced, explain why it is needed.
