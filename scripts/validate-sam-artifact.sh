#!/usr/bin/env bash
set -euo pipefail

FUNCTION_DIR="${1:-.aws-sam/build/JobeasyApiFunction}"
EXPECTED_DLL="${2:-Api.dll}"
ARTIFACT_PATH="${FUNCTION_DIR%/}/${EXPECTED_DLL}"

if [[ ! -d "$FUNCTION_DIR" ]]; then
  echo "[ERROR] SAM artifact directory not found: $FUNCTION_DIR"
  echo "Run: sam build -t infra/sam/template.yaml"
  exit 1
fi

if [[ ! -f "$ARTIFACT_PATH" ]]; then
  echo "[ERROR] Expected Lambda assembly not found: $ARTIFACT_PATH"
  echo "The Lambda handler is configured as '${EXPECTED_DLL%.dll}', so the DLL must exist at the artifact root (/var/task)."
  echo "Current artifact root contents:"
  find "$FUNCTION_DIR" -maxdepth 1 -type f -printf ' - %f\n' | sort || true
  exit 1
fi

echo "[OK] Lambda artifact validated: $ARTIFACT_PATH"
