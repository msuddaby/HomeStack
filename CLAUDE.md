# HomeStack

Self-hosted household app. C# backend (`backend/`, .NET 10, ASP.NET Core minimal APIs, EF Core + SQLite) and
React frontend (`frontend/`, Vite PWA, TanStack Query, Tailwind v4). Roadmap: `docs/PLAN.md`. Module how-to: `README.md`.

- Every feature is a module: backend `IModule` class library + frontend `WebModule`. Keep them self-contained.
- All data is household-scoped: filter by `ICurrentUser.HouseholdId` in every query.
- Request DTOs are positional records with DataAnnotations; add `.Validate<T>()` to the endpoint (`DtoValidator` handles record parameter attributes). Throw `NotFoundException`/`BadRequestException`/`ConflictException` from services.
- Dates without time use `DateOnly` ("YYYY-MM-DD" on the wire); "today" comes from `TimeProvider.Today()`.
- Phone-first UI: test layouts at ~390px wide; sheets (`Sheet`) instead of pages for quick actions.
- Verify: `cd backend && dotnet test`; `cd frontend && npm run typecheck && npm run build`.
- AI features go behind a provider interface (Claude or Ollama), not hard-wired.
