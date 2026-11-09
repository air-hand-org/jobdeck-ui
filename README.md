# jobdeck-ui

## Migration verification

`make lint` runs EF Core's `has-pending-model-changes` check so model changes
require a migration. CI also runs `scripts/test-migration-image.cs` against the
actual Dockerfile `migrator` target, on the Docker-capable image-build runner.
It applies that image to an empty temporary PostgreSQL 17 database, checks the
application tables and all migration history entries, then runs the same image
ID again and checks that history is unchanged. A rejected connection must return
a failing bundle exit code. Bundle output and PostgreSQL failure logs appear in
the smoke-test step; containers, volumes and the isolated network are removed.

To reproduce after building/loading the `migrator` target locally:

```sh
dotnet run --file scripts/test-migration-image.cs -- your-migrator-image:tag
```

Requires the pinned .NET toolchain and a running Docker daemon. No persistent
database or separate migrator project is needed.

## Licenses

Project code is licensed under [MIT](LICENSE). Third-party NuGet originals and
provenance are versioned in `third_party/licenses/nuget/`; regenerate them with
`make licenses` after restoring the project toolchain and package credentials.
CI regenerates and checks that the working tree stays clean. Unresolved
collection errors fail CI.

The runtime image includes the project license and NuGet documents under
`/usr/share/licenses/jobdeck-ui/`. Vendored Bootstrap retains its existing
license headers in the distributed static assets.
