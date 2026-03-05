.PHONY: build-JobeasyApiFunction sam-verify

build-JobeasyApiFunction:
	dotnet restore src/Api/Api.csproj
	dotnet publish src/Api/Api.csproj -c Release -f net8.0 --no-restore -o $(ARTIFACTS_DIR)

sam-verify:
	./scripts/validate-sam-artifact.sh
