.PHONY: nuget-license-trial
nuget-license-trial: cs-restore
	@dotnet tool restore
	@rm -rf third_party/nuget-license-trial
	@mkdir -p third_party/nuget-license-trial
	@dotnet tool run nuget-license -- -i src/ui.csproj -t -f net10.0 \
	    -d third_party/nuget-license-trial/downloads \
	    -o JsonPretty -fo third_party/nuget-license-trial/report.json
