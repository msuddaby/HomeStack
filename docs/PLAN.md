# HomeStack — Plan

A self-hosted, phone-first household app built from pluggable modules.

## Decisions

| Area | Choice | Why |
|---|---|---|
| Hosting | Self-hosted, single Docker container, `docker compose up` | Data stays home; one volume to back up |
| Users | Household with multiple user accounts sharing data | Everyone in the house sees the same fridge |
| Client | Phone-first PWA (installable, camera access) | One codebase; barcode scanning works in the browser |
| AI | Pluggable provider interface, added in a later phase | Works with Claude API or a local model (Ollama) |
| Backend | C# / ASP.NET Core (.NET 10) minimal APIs | |
| DB | SQLite + EF Core, migrations applied at startup | Zero-ops, one file, easy backup; EF makes Postgres a provider swap later |
| Frontend | React + TypeScript + Vite + React Router + TanStack Query + Tailwind, vite-plugin-pwa | |
| Auth | Username/password (ASP.NET PasswordHasher), httpOnly cookie auth | Nothing external to run |

> The camera (barcode/receipt scanning) only works over **HTTPS** on phones. Use Tailscale
> (`tailscale serve`) or a Caddy reverse proxy in front of the container.

## Repo layout

```
backend/
  src/HomeStack.Core               IModule contract, DbContext, households/users, auth, validation, errors
  src/HomeStack.Modules.Inventory  one class library per module: entities, service, endpoints
  src/HomeStack.Api                host: registers modules, owns EF migrations, serves the built frontend
  tests/HomeStack.Tests            xUnit integration tests (real API + throwaway SQLite)
frontend/
  src/core                         layout, auth, api client, UI kit, WebModule contract
  src/modules/<id>/                pages, dashboard widget, settings panel
```

### Module contract
Each module registers itself on both sides:
- **backend**: a class implementing `IModule` (`AddServices`, `ConfigureModel`, `MapEndpoints`, `OnHouseholdCreatedAsync`), mounted at `/api/<id>` behind auth. Its tables are prefixed `<id>_` and live in the shared `HomeStackDbContext`.
- **frontend**: a `WebModule` (`id, name, icon, path, routes, DashboardWidget?, Settings?`). The home dashboard pulls in every module's widgets, so new modules show up there automatically.

Modules may read each other through service functions (e.g. meal planning reads inventory), never through each other's tables directly.

## Modules and phases

### Phase 1: Core + Inventory ✅ (built 2026-11-14)
- Household setup on first run, user login, invite more users
- **Locations**: Pantry, Fridge, Freezer by default; add more (e.g. "Garage chest freezer")
- **Products** (what something *is*: name, category, barcode, default shelf life, low-stock threshold) vs.
  **Items** (a specific thing on a shelf: quantity, location, purchased/expiry/opened/frozen dates)
- **Fast entry**
  - Quick-add with autocomplete from products you've added before
  - Expiry date suggested from the category's shelf life (milk → +7 days, ground beef in freezer → +4 months)
  - Barcode scan via camera → product lookup in Open Food Facts (free, no key) → remembered for next time
- **Freezer**: "Move to freezer" records the date frozen and resets best-before to the freezer shelf life; "Thaw" moves it back with a short use-by
- **Actions**: use some or all, throw out (counted as waste), move, edit
- **Dashboard**: expired, expiring soon, *freeze it now* (freezable fridge items about to expire), *long in freezer*, low stock
- Event log (added/used/wasted/frozen) to support waste stats and shopping predictions later

### Phase 2: Shopping list ✅ (built 2026-11-14)
- Manual list, plus auto-add when a product drops below its low-stock threshold
- Check off at the store → "Put away" adds the checked items to inventory with suggested expiry dates (this becomes the main way groceries get entered)
- Built: store-section grouping, "2 milk" quantity parsing, optimistic check-off, 5s polling for shared shopping,
  dismissable auto items, product editor (minimums, shelf-life overrides), add-to-list from inventory
- Later: per-store aisle order, realtime push (SignalR) instead of polling, price tracking

### Phase 3: Recipes + Meal planning ✅ (built 2026-11-14)
- Recipes (ingredients linked to products, steps, servings, tags); import from a URL using schema.org Recipe metadata
- "What can I make?" ranks recipes by how many ingredients you have, weighted toward ingredients that expire soon
- Weekly meal plan; cooking a planned meal deducts its ingredients from inventory; missing ingredients go to the shopping list
- Built as one `meals` module (Plan · Recipes · What can I make?). Ingredient lines are parsed (fractions, units,
  package sizes, notes); names auto-link to products conservatively and links can be fixed per ingredient; salt/pepper/water
  are staples. Cooking converts within mass/volume/count units. URL import reads schema.org JSON-LD and only fetches public hosts.
- Later: pantry staples list per household, "have enough" quantity checks, nutrition, recipe photos upload

### Phase 4: AI (pluggable provider)
- ✅ Provider layer (built 2026-11-14): per-household settings; OpenAI-compatible format (DeepSeek default, OpenAI, Ollama,
  OpenRouter, LM Studio…) plus native Anthropic; connection test checks text and vision; `AiService.JsonAsync<T>` for modules
- ✅ Receipt scan + natural-language add (built 2026-11-14): photos (up to 4, shrunk on the phone) or typed text → review list
  (names, category, place, quantity, expiry) → add all in one transaction; known product names are fed to the model and matched.
  Receipts moved to the Spending module on 2026-11-17 (see Phase 5); typed lists stay here
- Fridge/pantry photo → suggested items to add
- ✅ Meal ideas (built 2026-11-14): "Use it up" on What can I make? and "Plan my week" on the plan. Saved recipes first,
  new ones otherwise; missing/expiring worked out against real stock; nothing saved until accepted (new ones tagged `idea`)
- An **MCP server** exposing HomeStack, so you can ask Claude "what's about to go off?" from any client

### Phase 5: Spending ✅ (built 2026-11-17)
- `expenses` module ("Spending"): stores, receipts, receipt lines, tracked items and each store's item numbers for them
- One receipt scanner (from Spending or Inventory → Add → Scan receipt): every line with item number, quantity, price and
  discount, plus store, tax and total; food lines can go into inventory in the same save (one transaction); receipt photos kept.
  Unticking "Put away" is remembered per item (a case of pop is tracked as spending, not inventory); "Snacks & drinks" category
- Same store + item number links to the same item automatically; across stores the AI suggests "same as X?" and the user confirms.
  Items can also be combined by hand ("Same as another item…")
- Overview by month (category incl. tax, store, six-month bars), receipts, item history with price per store and buying rhythm,
  manual expenses; "Usually buy about now" on Home and in the daily digest, with add-to-list
- ✅ Bills, splitting and recurring payments (built 2026-11-17):
  - Admins: the household's first member is admin; admins make others admin and mark split payments received
  - Splits: every expense has a "paid by" person and a split (household members plus people not on HomeStack, by name);
    each receipt line can have its own people; tax and unitemised amounts are shared in proportion; equal or custom amounts.
    "Owed" tab: who owes whom, admins tick shares received
  - Bills: upload a PDF (pages rendered to images with PDFtoImage) or photos → AI reads company, amount, dates → review → save
    with the original file; mark paid to the company; due-date reminders on Home and in the digest
  - Recurring: fixed payments (rent) recorded automatically on the day with their split; expected bills (amount varies)
    shown as missing until that period's bill is uploaded
- Later: budgets per category, edit receipt lines after saving, CSV export, "settle up" summaries

### Cross-cutting
- ✅ Docker deployment: restart policy, runs as host user, persistent sign-in keys (built 2026-11-14)
- ✅ Nightly SQLite backup (`VACUUM INTO`) to a mounted folder with retention; Settings → Backups (built 2026-11-14)
- ✅ Daily digest via ntfy: modules contribute sections through `IDigestSource`; background jobs act for a household
  through `HouseholdScope` (built 2026-11-14).
- ✅ Native notifications (Web Push, built 2026-11-17): each member turns them on per device (iPhone: from the Home Screen
  app); own VAPID key, messages encrypted on the server, no third party. The digest goes to every member's devices, ntfy
  is optional. Event notifications (`ExpenseNotifier`): "you owe" when a
  share is new or bigger (not to whoever made the change), a bill going overdue (once, to its payer or everyone), and an
  admin's "Remind" (once an hour per debt). Later: per-member choice of which notifications they get
- ✅ Multiple households per instance (built 2026-11-17): the instance admin (`Admin:Usernames`, default `matt`) invites
  new households by link (one use, expiring; only a hash is stored), renames them, resets passwords and deletes them
  (`HouseholdDeleter` clears every table with a `HouseholdId`, so new modules are covered), but can't see their data.
  The admin picks households to share their AI with (`Household.UseSharedAi`; used only while a household has none of its own). Backups are instance-admin only.
  Later: household admins inviting their own members by link
- Waste stats: money/items thrown out per month

### Future module ideas
Home maintenance (filters, smoke detector batteries, seasonal tasks), chores, warranties and manuals,
household documents, plant care.
