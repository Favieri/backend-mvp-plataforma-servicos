#!/usr/bin/env bash
set -euo pipefail

TEMPLATE_PATH="${TEMPLATE_PATH:-infra/sam/template.yaml}"
FUNCTION_DIR="${FUNCTION_DIR:-.aws-sam/build/JobeasyApiFunction}"
EXPECTED_DLL="${EXPECTED_DLL:-Api.dll}"
ARTIFACT_PATH="${FUNCTION_DIR%/}/${EXPECTED_DLL}"

sam build -t "$TEMPLATE_PATH"

if [[ ! -d "$FUNCTION_DIR" ]]; then
  echo "[ERROR] SAM artifact directory not found after build: $FUNCTION_DIR"
  exit 1
fi

if [[ ! -f "$ARTIFACT_PATH" ]]; then
  echo "[ERROR] Expected Lambda assembly not found: $ARTIFACT_PATH"
  echo "Handler expects '${EXPECTED_DLL%.dll}', therefore ${EXPECTED_DLL} must be at artifact root (/var/task)."
  echo "Artifact root files:"
  find "$FUNCTION_DIR" -maxdepth 1 -type f -printf ' - %f\n' | sort || true
  exit 1
fi

echo "[OK] Lambda artifact validated: $ARTIFACT_PATH"
echo "[INFO] Artifact root listing:"
find "$FUNCTION_DIR" -maxdepth 1 -printf '%P\n' | sed '/^$/d' | sort
