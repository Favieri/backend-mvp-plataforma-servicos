.PHONY: build-JobeasyApiFunction sam-verify

build-JobeasyApiFunction:
	dotnet restore src/Api/Api.csproj
	dotnet publish src/Api/Api.csproj -c Release -f net8.0 --no-restore -o $(ARTIFACTS_DIR)

sam-verify:
	sam build -t infra/sam/template.yaml
	./scripts/validate-sam-artifact.sh
