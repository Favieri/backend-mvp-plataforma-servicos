#!/usr/bin/env bash
set -euo pipefail

sam build -t infra/sam/template.yaml

ARTIFACT_DLL=".aws-sam/build/JobeasyApiFunction/Api.dll"

if ! test -f "$ARTIFACT_DLL"; then
  echo "[ERROR] Expected Lambda assembly not found: $ARTIFACT_DLL"
  echo "[ERROR] Contents of .aws-sam/build/JobeasyApiFunction/:"
  ls -la .aws-sam/build/JobeasyApiFunction/ || true
  exit 1
fi

echo "[OK] Lambda artifact validated: $ARTIFACT_DLL"
