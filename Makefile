.PHONY: build-JobeasyApiFunction sam-verify

build-JobeasyApiFunction:
	dotnet publish src/Api/Api.csproj \
		-c Release \
		-f net8.0 \
		--no-self-contained \
		/p:GenerateRuntimeConfigurationFiles=true \
		/p:GenerateDependencyFile=true \
		-o $(ARTIFACTS_DIR)
	@echo "--- Validando artefatos obrigatórios ---"
	@test -f $(ARTIFACTS_DIR)/Api.dll          || (echo "ERRO: Api.dll ausente"          >&2; exit 1)
	@test -f $(ARTIFACTS_DIR)/Api.deps.json    || (echo "ERRO: Api.deps.json ausente"    >&2; exit 1)
	@test -f $(ARTIFACTS_DIR)/Api.runtimeconfig.json || (echo "ERRO: Api.runtimeconfig.json ausente" >&2; exit 1)
	@echo "Artefatos OK."

sam-verify:
	bash scripts/validate-sam-artifact.sh
