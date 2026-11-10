# jobdeck-ui

## Browser/server boundary

Job pages and forms run as Blazor WebAssembly, without a SignalR circuit.
The existing UI server remains the same-origin BFF and statically renders account
pages. OAuth callbacks, token exchange, database-backed sessions, certificates,
and gRPC clients stay on the server. The client project never references the
server project or backend contracts.

The browser calls only the fixed `/bff/jobs` and job execution endpoints. They
require a validated session cookie; mutations additionally require an ASP.NET
Core antiforgery cookie/token pair (`X-CSRF-TOKEN`). The BFF validates form data
and explicitly maps browser DTOs to/from protobuf messages. Backend job ownership
authorization still applies using the session's access token. No generic RPC,
method/type selection, browser bearer token, or localStorage token is exposed.
Backend exception details are not returned to the browser.

Reads can be retried after connection failures. Execution polling is sequential
and cancelled when leaving its page. Mutations are never automatically retried;
a lost response has an unknown outcome. Reload/read the current state before
submitting again. An expired session sends the browser back to the SSR login.
Logout removes the server session, invalidating other tabs' cookies as well.

Deployment keeps the existing UI container, origin, port and PostgreSQL session
store. This migration requires no separate browser host, DNS, IAM or CORS rule.
`Dockerfile` publishes both projects through `src/ui.csproj`. CI checks the
published browser assets with `scripts/check-browser-assets.sh`; the existing
image build/publish workflows still build the `ui` and `migrator` targets.

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
