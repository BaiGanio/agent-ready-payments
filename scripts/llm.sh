#!/usr/bin/env bash
#
# Starts llama-server with the model named in .env, so the model lives in one file and not
# in a command you retype. The agent never learns the model's name: it talks to LLM_URL and
# llama-server serves whatever was loaded here.
#
# Usage:
#   ./scripts/llm.sh                         # the model in LLM_HF_MODEL
#   ./scripts/llm.sh unsloth/other-GGUF:Q4   # one run with another model, .env untouched
#
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# One value out of .env. The file is read, never sourced: sourcing it would export the API
# keys and the SA password into the model server, which has no business with any of them.
from_env() {
  [[ -f "$root/.env" ]] || return 0

  sed -n "s/^[[:space:]]*$1=//p" "$root/.env" | tail -1
}

# An argument wins, then the shell, then .env, then the default. That order matters: a value
# you exported for one terminal must not be overruled by a file.
model="${1:-${LLM_HF_MODEL:-$(from_env LLM_HF_MODEL)}}"
ctx="${LLM_CTX:-$(from_env LLM_CTX)}"
port="${LLM_PORT:-$(from_env LLM_PORT)}"

if [[ -z "$model" ]]; then
  echo "No model. Put LLM_HF_MODEL in .env (copy the line from .env.example)," >&2
  echo "or pass one: ./scripts/llm.sh <hf-repo>:<quant>" >&2
  exit 2
fi

if ! command -v llama-server >/dev/null; then
  echo "llama-server is not on your PATH. Install llama.cpp: brew install llama.cpp" >&2
  exit 2
fi

# --jinja is not optional. Without the model's own chat template the model prints tool calls
# as plain text, the loop refuses every one of them as a leak, and nothing works.
echo "llama-server: $model  (context ${ctx:-16384}, port ${port:-8080})"

exec llama-server -hf "$model" --jinja -c "${ctx:-16384}" --port "${port:-8080}"
