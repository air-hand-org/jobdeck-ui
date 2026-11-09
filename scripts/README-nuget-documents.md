# NuGet original-document collector

Run `make licenses` after restoring packages. The collector uses bundled original
license/attribution documents first. If no bundled license is present, it lists
that repository's root at the nuspec's exact 40-character commit, then retrieves
only standard root document names present as regular files (including common
title-case names such as Roslyn's `License.txt`). It uses a non-recursive Git tree
API call, not a clone; symlinks/submodules are not followed and truncated or
malformed listings are errors.

Only if that commit has no original license (all probes completed without
retrieval errors), or the nuspec commit is missing/invalid, it looks for the exact
`VERSION` and `vVERSION` tags on the validated GitHub repository. Annotated tags
are peeled to a commit. Both aliases may exist if they resolve to the same SHA;
different SHAs, missing tags, malformed responses and API failures stay unresolved.
There is no default-branch fallback. A successful commit collection does not query
tags. HTTP/auth/transport failures are not converted into missing documents or
hidden by a tag fallback.

Tag/tree API access uses .NET HTTP directly (no GitHub CLI/helper dependency). Optional
`GITHUB_TOKEN` authentication is sent only to `api.github.com`, never to raw
document URLs; redirects are disabled. CI supplies its read-only job token.
Without a token the lower public API quota also applies to root listings; rate
limit/auth/transport errors remain failures rather than missing documents.
Raw document retrieval is SHA-pinned. `NOTICE.html`
and `notice.html` are candidates, and HTML is accepted only for those exact
names on pinned raw GitHub URLs. Four package workers share a four-request limit
across API and raw retrieval (including retries). API and document tasks are
single-flight cached for the duration of a run, including failures, so concurrent
packages at the same repository/revision do not duplicate requests. Package and
document output ordering is independent of task completion order.

The manifest preserves nuspec metadata, root-listing status and each actually
attempted document URL/status (not HTTP probes of names absent from the listing),
selected tag, resolved SHA, provenance, fallback reason, both tag candidates and
the nuspec/tag SHA mismatch flag. Source documents live under
`source/PACKAGE/VERSION/SHA/NAME`, so documents retained from both attempted
revisions cannot overwrite one another. A missing nuspec commit has a null
mismatch flag rather than a fabricated comparison.

Collection errors produce exit status 2 and remain in `manifest.json`. Root
candidate probing is not a complete audit of nested attribution, and packaged
licenses still suppress source probing. Existing generated manifests are not
rewritten merely by changing the collector; regenerate with a complete restore.

## Tests

Requires .NET 10 only; no Python, external test packages or GitHub/network access:

```sh
dotnet run --file scripts/run-nuget-source-tests.cs
```

The runner compiles the collector's actual resolver and download function in a
temporary file-based app with deterministic API/HTTP fixtures. It also runs the
complete collector on synthetic nupkg inputs to verify bundled documents, unsafe
repository rejection and unresolved exit status. No alternate resolver is kept
in the tests. Temporary files use `TMPDIR` when configured. Concurrent fixtures
also verify the shared request bound, single-flight success/failure caching,
listing safety and deterministic output.

## Live benchmark

Use the same authentic restored package inventory/cache for both source versions:

```sh
dotnet run --file scripts/benchmark-nuget-documents.cs -- BEFORE.cs scripts/collect-nuget-documents.cs INVENTORY.json RESTORED_CACHE OUTPUT
```

The harness reports collection time (excluding compilation/startup), real HTTP
request count and response-body bytes, including retries/404s. It fails if any
original file bytes or manifest fields differ, excluding only `sourceAttempts`
which intentionally describes the changed discovery policy. Root listing JSON
can increase body bytes even while eliminating many round trips. Network timings
are environment-dependent; do not extrapolate a public subset to a full restore.
