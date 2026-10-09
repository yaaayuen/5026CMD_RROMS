# Shared contracts (frozen at the end of Sprint 0)

These are the agreements that let five sprints run in parallel. Changing anything here needs a
pull request approved by **every member whose module uses it**.

## 1. Architecture

```
Razor Pages (UI)  →  Services (business rules)  →  ApplicationDbContext (EF Core)  →  SQL Server
```

- **Pages never use `ApplicationDbContext` directly.** They call a service interface, e.g. `IReservationService`.
- Services that can fail for a business reason return `ServiceResult` / `ServiceResult<T>`
  (`Common/ServiceResult.cs`). The PageModel copies `result.Errors` into `ModelState`.
- Unexpected failures (database down) are exceptions and reach the error page.
- Register your service in `Program.cs` as **scoped**: `builder.Services.AddScoped<IMenuService, MenuService>();`

## 2. Folder layout

| Folder | What goes there |
|---|---|
| `Areas/Admin/Pages`, `Areas/Staff/Pages`, `Areas/Customer/Pages` | Pages for that role. Put your module in a subfolder, e.g. `Areas/Admin/Pages/Tables/` |
| `Pages/` | Public pages (home, menu browsing, account pages) |
| `Models/` | Entity classes (one per file) |
| `Models/ViewModels/` | Classes that exist only to feed a view |
| `Services/` | `IXxxService` interface + `XxxService` implementation, one file each |
| `Contracts/` | Shared enums (do not edit without agreement) |
| `Options/` | Settings classes bound from `appsettings.json` |
| `Data/` | `ApplicationDbContext`, seeder, migrations |

## 3. Roles, access and the current user

Use `RoleNames.Administrator`, `RoleNames.Staff`, `RoleNames.Customer`. Never type the strings.

**Access is already enforced by folder (YY-3)** in `Program.cs`, so you do not add `[Authorize]` to pages:

| Folder | Who can open it |
|---|---|
| `Areas/Admin/Pages/**` | Administrator |
| `Areas/Staff/Pages/**` | Staff or Administrator |
| `Areas/Customer/Pages/**` | Customer |
| `/Account/Profile`, `/Account/ChangePassword`, `/Notifications` | any signed-in user |
| everything else in `Pages/` | everyone (public) |

**Current user rule (ownership):** get the signed-in user's ID with `User.GetUserId()`
(`using Rroms.Web.Security;`). Never take a user ID from a form, query string or hidden field.
Pass it to your service and filter by it, e.g. `.Where(r => r.CustomerId == userId)`.
If a record is not found **or belongs to someone else**, return 404 in both cases.

## 4. Status values (`Contracts/`)

| Enum | Values | Owner of transitions |
|---|---|---|
| `ReservationStatus` | Pending, Confirmed, Seated, Completed, Cancelled, NoShow | WL |
| `OrderStatus` | Placed, Preparing, ReadyToServe, Served, Completed, Cancelled | YJ creates (Placed); Alex owns the rest |
| `PaymentStatus` | Unpaid, Paid, Voided | Darren |
| `PaymentMethod` | Cash, CardTerminal, EWallet, SimulatedOnline | Darren |
| `NotificationType` | General, ReservationConfirmed, ReservationUpdated, ReservationCancelled, OrderUpdated, PaymentConfirmed, Promotion | each publisher |

All enums are stored as **text** in SQL Server (configured once in `ApplicationDbContext.ConfigureConventions`).

## 5. Notifications

Inject `INotificationService` and call it after your change is saved:

```csharp
await _notificationService.NotifyUserAsync(
    reservation.CustomerId,
    NotificationType.ReservationConfirmed,
    "Reservation confirmed",
    $"Your table for {reservation.PartySize} is confirmed.",
    $"/Customer/Reservations/Details/{reservation.Id}");
```

What happens (YY-6): the notification is saved to the user's inbox, pushed live to any open tab
(SignalR hub at `/hubs/notifications`), and emailed through a background queue. A Gmail failure never
breaks your action. **Call it after your own `SaveChangesAsync`**, because it saves on the shared DbContext.
`NotifyRoleAsync(RoleNames.Staff, ...)` reaches every active staff member.
Live tracking (AL-3) can reuse `IHubContext<NotificationHub>` with its own message name.

## 6. Reservation rules (`Options/ReservationRulesOptions.cs`)

Visit 120 min · start times every 30 min · up to 90 days ahead · party size 1–20.
Inject `IOptions<ReservationRulesOptions>`; never hard-code these numbers.

## 7. Database rules

- Money is `decimal` (stored as `decimal(10,2)` automatically). Never `double` for money.
- Timestamps are stored in **UTC** (`DateTime.UtcNow`, property names end in `Utc`).
  Convert for display with `MalaysiaTime.FromUtc(...)`; convert form input with `MalaysiaTime.ToUtc(...)`.
- Foreign keys to users are `string CustomerId` / `string StaffId` (Identity IDs are strings).
- Migration names: `S<sprint>_<owner>_<feature>`, e.g. `Add-Migration S2_WL_Reservations -OutputDir Data/Migrations`.
- Before adding a migration, **pull main first** so your migration builds on everyone else's.
- `EnableRetryOnFailure` is on. If you open a transaction yourself (`BeginTransactionAsync`),
  wrap it in `_context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, otherwise EF throws.

## 8. Code style

- Explicit constructors with `private readonly` fields (no primary constructors).
- One class per file; braces on every `if`; `async`/`await` throughout.
- `/// <summary>` on every public class and method; inline comments explain **why**.
- No magic numbers: use constants or `Options` classes.
- Server-side validation always, even when the form also validates in the browser.
