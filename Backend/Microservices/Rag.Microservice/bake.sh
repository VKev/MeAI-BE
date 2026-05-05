#!/usr/bin/env bash
# Re-bake the knowledge base into ./src/bakedknowledge/.
#
# What it does:
#   1. Builds the rag-microservice image (in bake mode).
#   2. Spins up an empty Qdrant + a one-shot rag-microservice via
#      bake.compose.yml. The rag container runs the knowledge bootstrap once,
#      exports the resulting Qdrant points + LightRAG state to ./src/bakedknowledge/,
#      and exits.
#   3. Tears the stack down (no volumes left behind).
#
# Cost: ~1 LLM-extract call per ## section in src/knowledge/*.md (entity +
# relation extraction) + 1 embedding call per chunk. ~$0.02-0.10 per full
# bake on OpenRouter at current pricing. Subsequent runs that change only a
# few sections cost much less because the fingerprint cache skips unchanged
# ones — but since the bake's Qdrant is fresh-empty by design, the LightRAG
# llm_response_cache (kv_store_llm_response_cache.json) carried over from the
# previous bake is what makes reruns cheap, NOT the fingerprint registry.
# (The registry still works because the bake mounts and reads the previous
# baked rag_state if present.)
#
# Required env (or set in a .env beside this file):
#   LLM_API_KEY    your OpenRouter key (required)
#   LLM_BASE_URL   defaults to https://openrouter.ai/api/v1
#   EMBED_MODEL    defaults to openai/text-embedding-3-small
#   EMBED_DIM      defaults to 1536
#
# After completion:
#   git status src/bakedknowledge   # review what changed
#   git add src/bakedknowledge
#   git commit -m "rebake knowledge"

set -euo pipefail

cd "$(dirname "$0")"

if [ -z "${LLM_API_KEY:-}" ]; then
  echo "ERROR: LLM_API_KEY is not set." >&2
  echo "Export your OpenRouter key first, e.g.:" >&2
  echo "  export LLM_API_KEY=sk-or-v1-..." >&2
  exit 1
fi

COMPOSE_FILE="bake.compose.yml"

# Make sure the host-mount target exists so docker doesn't create it as root.
mkdir -p src/bakedknowledge

echo "→ Building bake image..."
docker compose -f "$COMPOSE_FILE" build rag-bake

echo "→ Running bake (this will run knowledge bootstrap once, ~3-15 min)..."
# --abort-on-container-exit: when rag-bake exits (success or fail), tear down qdrant
# --exit-code-from rag-bake: propagate rag-bake's exit code to this script
exit_code=0
docker compose -f "$COMPOSE_FILE" up \
  --abort-on-container-exit \
  --exit-code-from rag-bake || exit_code=$?

echo "→ Cleaning up..."
docker compose -f "$COMPOSE_FILE" down --remove-orphans

if [ "$exit_code" -ne 0 ]; then
  echo ""
  echo "✗ Bake FAILED (exit code $exit_code). See logs above." >&2
  exit "$exit_code"
fi

echo ""
echo "✓ Bake complete. Review changes:"
echo "    git diff src/bakedknowledge"
echo "  Then commit:"
echo "    git add src/bakedknowledge && git commit -m 'rebake knowledge'"
