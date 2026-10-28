.PHONY: nuget-license-trial
nuget-license-trial: cs-restore
	@dotnet tool restore
	@rm -rf third_party/nuget-license-trial
	@mkdir -p third_party/nuget-license-trial
	# Exploratory output only: preserve the observed two validation errors for review.
	@set -e; status=0; dotnet tool run nuget-license -- -i src/ui.csproj -t -f net10.0 \
	    -d third_party/nuget-license-trial/downloads \
	    -o JsonPretty -fo third_party/nuget-license-trial/report.json || status=$$?; \
	    jq . third_party/nuget-license-trial/report.json; \
	    echo "nuget-license validation exit: $$status"; \
	    test "$$status" -eq 0 -o "$$status" -eq 2
