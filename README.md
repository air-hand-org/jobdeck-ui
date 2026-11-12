# jobdeck-ui

## Browser/server boundary

Job pages and forms run as Blazor WebAssembly, without a SignalR circuit.
`src/client/ui.Client.csproj` contains browser code; `src/bff/ui.csproj` hosts
WASM assets, statically rendered account pages and browser API endpoints in one
ASP.NET Core process on the same port. OAuth callbacks, token exchange,
database-backed sessions, certificates and backend gRPC connections stay server-side.

Job pages prerender authenticated initial HTML. A request-scoped read-only client
calls the existing backend clients directly, without self-HTTP requests. Public
page data is transferred to WASM via component `[PersistentState]` properties,
so hydration does not repeat the initial reads. The list transfers only its
visible ID/name/enabled fields; forms and execution rows transfer their existing
browser-facing models, never session tokens or backend credentials. Job-page
responses use `Cache-Control: no-store`. Polling and mutation controls start only
after WASM becomes interactive; there is no Circuit or per-connection server UI state.

The job list uses the existing `jobdeck.job.v1.JobService/ListJobs` contract over
gRPC-Web (including HTTP/1.1). This is the only implemented browser RPC; all other
methods of that generated service return UNIMPLEMENTED and do not reach the
backend. The endpoint requires a session cookie and the ASP.NET Core antiforgery
cookie/token pair (`X-CSRF-TOKEN`). The BFF calls the backend through the session's
access token, preserving job ownership authorization. This trial intentionally
references `Jobdeck.Job.Contracts` from the browser; a separate `jobdeck.bff.v1`
contract is a later step. Backend auth contracts and credentials are not browser assets.

Other job operations and execution polling retain fixed JSON `/bff/jobs`
endpoints. Mutations require antiforgery validation and server-side form validation.
No generic RPC forwarding, browser bearer token or localStorage token is exposed.
Backend exception details are not returned to the browser.

Reads can be retried after connection failures. Execution polling is sequential
and cancelled when leaving its page. Mutations are never automatically retried;
a lost response has an unknown outcome. Reload/read the current state before
submitting again. An expired session sends the browser back to the SSR login.
Logout removes the server session, invalidating other tabs' cookies as well.

Deployment keeps the existing UI container, origin, port and PostgreSQL session
store. This migration requires no separate browser host, DNS, IAM or CORS rule.
`Dockerfile` publishes both projects through `src/bff/ui.csproj`. CI checks the
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
