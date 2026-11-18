# HomeStack

A self-hosted, phone-first household app made of modules:

- **Inventory**: what food you have, where it is, and what needs eating or freezing.
- **Shopping**: a shared list that fills itself from low stock; "Put away" moves bought items into inventory.
- **Meals**: recipes (import from a link), "what can I make?", and a weekly plan; cooking deducts ingredients.
 See [docs/PLAN.md](docs/PLAN.md) for the roadmap.

- **Backend:** C# / ASP.NET Core (.NET 10), EF Core + SQLite (`backend/`)
- **Frontend:** React + TypeScript + Vite PWA (`frontend/`)

## Run it (Docker)

```sh
cp .env.example .env      # set TZ (and your user id if it isn't 1000)
docker compose up -d --build
```

Open `http://<server>:8080`. On first visit you create the household and your account. Add other people
under **Settings → Household members**. The container restarts on its own after reboots and crashes.

- **Data:** `./data` holds the live database and the sign-in keys.
- **Backups:** every night after 3am the app writes `homestack-YYYY-MM-DD.db` to `./backups`, keeping 14
  (change with `HOMESTACK_BACKUPS` and `HOMESTACK_BACKUP_KEEP` in `.env`). Point it at another disk or a synced
  folder for an off-machine copy. **Settings → Backups** shows the last one and has *Back up now*.
  To restore, stop the container, copy a backup over `data/homestack.db` (deleting `homestack.db-wal`/`-shm`), and start it.
- **Daily reminder:** **Settings → Daily reminder** sends a morning push via [ntfy](https://ntfy.sh)
  (free app; use ntfy.sh or your own server) listing what to freeze, what's expiring and what's on the menu.
- **AI (optional):** **Settings → AI** connects any provider: DeepSeek (default, `deepseek-flash`), Claude, OpenAI,
  a local Ollama (`http://host.docker.internal:11434/v1`), or anything OpenAI-compatible. *Test connection* checks
  that the model can read photos. Then **Inventory → + → Scan receipt** or **Type a list** reads items for you to review,
  and **Meals** gets *Use it up* ideas and *Plan my week*, built around what's expiring.
- **More households:** one server can hold several households, each private to its members. The HomeStack admin
  (username `matt` by default; change with `Admin__Usernames__0` in the container's environment) gets
  **Settings → Manage HomeStack**: invite links that let someone set up their own household, renaming, resetting a
  forgotten password, deleting a household, and choosing which households may use their AI (until they set up their
  own). The admin can't see inside other households. Admin usernames are reserved, and only the admin sees backups
  (they hold every household).
- **Update:** `git pull && docker compose up -d --build`.

**Phones and the camera:** browsers only allow camera access (barcode scanning) over HTTPS. The easiest
way to get HTTPS on a home network is [Tailscale](https://tailscale.com): `tailscale serve --bg 8080`, then
open the `https://<machine>.<tailnet>.ts.net` URL on your phone and choose *Add to Home Screen*.

## Develop

```sh
# terminal 1: API on http://localhost:5080 (applies migrations, DB in ./data-dev)
cd backend && dotnet run --project src/HomeStack.Api

# terminal 2: UI on http://localhost:5173 (proxies /api to 5080)
cd frontend && npm install && npm run dev
```

Tests: `cd backend && dotnet test`. Frontend type-check: `cd frontend && npm run typecheck`.

The local dev database already has a test household: username `dev`, password `devpassword`.
To start fresh, delete `data-dev/`. Dev and Docker use separate databases.

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
3. **AI (optional):** inject `AiService` and call `JsonAsync<T>(new AiPrompt(system, user, images))`. It uses whatever
   provider the household picked; throw nothing provider-specific. Results are suggestions for the user to review.
4. **Daily reminder (optional):** register a scoped `IDigestSource` in `AddServices` returning `DigestSection`s;
   it runs with the household's `ICurrentUser`, so your normal services work. Empty sections are dropped.

Modules talk to each other through services (e.g. meal planning injects `InventoryService`), not by
querying each other's tables.
