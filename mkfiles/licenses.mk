.PHONY: licenses
licenses: cs-restore
	@set -eu; inventory=$$(mktemp); trap 'rm -f "$$inventory"' EXIT; \
	    dotnet list src/ui.csproj package --include-transitive --format json --no-restore > "$$inventory"; \
	    dotnet run --file scripts/collect-nuget-documents.cs -- "$$inventory" \
	    "$${NUGET_PACKAGES:-$$HOME/.nuget/packages}" third_party/licenses/nuget
