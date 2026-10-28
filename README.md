# jobdeck-ui

## Licenses

Project code is licensed under [MIT](LICENSE). Third-party NuGet originals and
provenance are versioned in `third_party/licenses/nuget/`; regenerate them with
`make licenses` after restoring the project toolchain and package credentials.
CI regenerates and checks that the working tree stays clean. Unresolved
collection errors fail CI.

The runtime image includes the project license and NuGet documents under
`/usr/share/licenses/jobdeck-ui/`. Vendored Bootstrap retains its existing
license headers in the distributed static assets.
