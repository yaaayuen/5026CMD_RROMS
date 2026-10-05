# Three Grills — Restaurant Reservation and Order Management System (RROMS)

5026CMD group coursework. ASP.NET Core Razor Pages (C#), Bootstrap, SQL Server.

| Sprint | Owner | Module |
|---|---|---|
| S1 | YY | Accounts, roles, profiles, notifications, base layout |
| S2 | WL | Restaurant setup, reservations, occupancy analytics, dashboard |
| S3 | YJ | Menu, ordering, recommendations, customer history, theme |
| S4 | Alex | Staff order queue, live tracking, feedback, announcements, chatbot |
| S5 | Darren | Billing and payments, staff reservation views, reports, exports, deployment |

Shared rules every member must follow: [docs/shared-contracts.md](docs/shared-contracts.md).

## First-time setup (Visual Studio 2026)

1. **Clone** the repository and open `Rroms.slnx`.
2. **Set your local secrets.** Right-click `Rroms.Web` > **Manage User Secrets** and paste:
   ```json
   {
     "BootstrapAdmin": {
       "Email": "admin@threegrills.local",
       "Password": "Choose-Your-Own-1!"
     }
   }
   ```
   The password needs 10+ characters with upper case, lower case, a digit and a symbol.
   Secrets are stored outside the project folder and are **never committed**.
3. **Create the database** (once the initial migration is on `main`, see below). Running the app in Development applies migrations automatically
   (`Database:ApplyMigrationsOnStartup`). To do it by hand instead:
   **Tools > NuGet Package Manager > Package Manager Console**, then `Update-Database`.
4. **Run** with the `https` profile (F5). Sign in as the bootstrap admin once YY-2 is merged.
5. **Inspect the data** in SQL Server Management Studio: connect to `(localdb)\MSSQLLocalDB`,
   database `ThreeGrillsRroms`.

## One-off step for YY-1 (only YY, only once)

The initial migration must be generated on a machine with the EF tools:

```
Add-Migration S1_YY_InitialIdentity -OutputDir Data/Migrations
```

Commit the generated `Data/Migrations` folder. Everyone else just pulls and runs.

## Running the tests

**Test > Test Explorer > Run All**, or from a terminal in the solution folder:

```
dotnet test
```

Tests use an in-memory SQLite database, so they need no SQL Server.

## Working agreements (short version)

- Branch per task: `feature/YY-2-login`. Commit under **your own** GitHub account.
- Open a pull request into `main`. It merges only when CI is green **and** your reviewer approves
  after you walk them through it (explain-before-merge).
- Review loop: YY → YJ → Darren → WL → Alex → YY.
- Never commit passwords, connection strings with passwords, or API keys.
- Any AI-assisted code is declared on the GenAI declaration form, and you must be able to explain it.
