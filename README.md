# jobdeck-ui

## Application structure

- `src/client/ui.Client.csproj`: Blazor WebAssembly pages, forms and browser gRPC-Web clients.
- `src/bff/ui.csproj`: ASP.NET Core host for prerendering, WASM assets, account pages and browser-facing gRPC-Web services on the same origin and port.

Authentication sessions, backend access tokens and certificates remain server-side.
Job pages prerender initial data and run interactively in the browser without a
SignalR circuit. Browser RPCs use session cookies and antiforgery tokens.

## Migrations

Manage database migrations with `dotnet ef migrations`. To execute migrations,
set `MIGRATOR_EXECUTE=true` and configure PostgreSQL using
`ConnectionStrings__Postgres` or the `POSTGRES_HOST`, `POSTGRES_PORT`,
`POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` environment variables.

## Licenses

Project code is licensed under [MIT](LICENSE). Third-party NuGet originals and
provenance are versioned in `third_party/licenses/nuget/`; regenerate them with
`make licenses` after restoring the project toolchain and package credentials.
CI regenerates and checks that the working tree stays clean. Unresolved
collection errors fail CI.

The runtime image includes the project license and NuGet documents under
`/usr/share/licenses/jobdeck-ui/`. Vendored Bootstrap retains its existing
license headers in the distributed static assets.
