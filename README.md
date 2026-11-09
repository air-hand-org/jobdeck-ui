# jobdeck-ui

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
