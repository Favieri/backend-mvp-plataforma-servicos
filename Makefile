.PHONY: build-JobeasyApiFunction sam-verify

build-JobeasyApiFunction:
	dotnet publish src/Api/Api.csproj -c Release -o $(ARTIFACTS_DIR)

sam-verify:
	bash scripts/validate-sam-artifact.sh
