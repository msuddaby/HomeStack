# HomeStack

A self-hosted, phone-first household app made of modules. The first module is **Inventory**: what food you
have, where it is, and what needs eating or freezing. See [docs/PLAN.md](docs/PLAN.md) for the roadmap.

- **Backend:** C# / ASP.NET Core (.NET 10), EF Core + SQLite (`backend/`)
- **Frontend:** React + TypeScript + Vite PWA (`frontend/`)

## Run it (Docker)

```sh
TZ=America/Toronto docker compose up -d --build
```

Open `http://<server>:8080`. On first visit you create the household and your account. Add other people
under **Settings → Household members**. Data lives in `./data/homestack.db`; back up that folder.

**Phones and the camera:** browsers only allow camera access (barcode scanning) over HTTPS. The easiest
way to get HTTPS on a home network is [Tailscale](https://tailscale.com): `tailscale serve --bg 8080`, then
open the `https://<machine>.<tailnet>.ts.net` URL on your phone and choose *Add to Home Screen*.

## Develop

```sh
# terminal 1: API on http://localhost:5080 (applies migrations, DB in ./data)
cd backend && dotnet run --project src/HomeStack.Api

# terminal 2: UI on http://localhost:5173 (proxies /api to 5080)
cd frontend && npm install && npm run dev
```

Tests: `cd backend && dotnet test`. Frontend type-check: `cd frontend && npm run typecheck`.

The local dev database already has a test household: username `dev`, password `devpassword`.
To start fresh, delete `data/`.

### Database changes

```sh
cd backend
dotnet tool restore
dotnet ef migrations add <Name> -p src/HomeStack.Api -s src/HomeStack.Api -o Migrations
```

Migrations are applied automatically on startup.

## Adding a module

1. **Backend:** `dotnet new classlib -n HomeStack.Modules.<Name> -o src/HomeStack.Modules.<Name>`, reference
   `HomeStack.Core`, add `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, and implement `IModule`
   (entities in `ConfigureModel` with tables prefixed `<id>_`, endpoints in `MapEndpoints`, scoped by
   `ICurrentUser.HouseholdId`). Register it in the `modules` array in `src/HomeStack.Api/Program.cs`, then add a migration.
2. **Frontend:** create `src/modules/<id>/index.ts` exporting a `WebModule` and add it to `src/modules/index.ts`.
   It gets a nav tab, its routes under `/<path>`, and its `DashboardWidget` on Home automatically.

Modules talk to each other through services (e.g. meal planning will inject `InventoryService`), not by
querying each other's tables.
