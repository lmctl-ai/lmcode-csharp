#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts
"${LMSCRIPT_BIN:-lmscript}" --no-preflight examples/RuleEntries/rules.lms artifacts/rule-entries.dslb
