#!/usr/bin/env bash
# Re-bake the knowledge base into ./src/bakedknowledge/.
#
# Drives the `bake` profile of `Backend/Compose/docker-compose-production.yml`:
# spins up `qdrant-bake` (empty disposable Qdrant on its own bake-net) +
# `rag-bake` (one-shot rag-microservice in bake mode), runs the bootstrap
# once, exports artifacts to ./src/bakedknowledge/, then stops + removes
# only those two services. Other prod services on the same compose file
# are NOT touched.
#
# Cost: ~$0.02-0.10 per full bake on OpenRouter (one LLM extract +
# one embed per ## section in src/knowledge/*.md). Reruns that change a
# few sections cost much less because the bake's qdrant-bake is fresh-empty
# every time but the WORKING_DIR is on a tmpfs; the LightRAG llm_response_cache
# from the previous bake (committed in src/bakedknowledge/rag_state/) is not
# carried in here, so each bake is a full fresh run. Acceptable — bakes are rare.
#
# All API keys are hardcoded in docker-compose-production.yml's `rag-bake`
# environment block (gitignored), so this script needs zero env setup.
#
# After completion:
#   git status src/bakedknowledge   # review changes
#   git add src/bakedknowledge
#   git commit -m "rebake knowledge"

set -euo pipefail

cd "$(dirname "$0")"

COMPOSE_FILE="../../Compose/docker-compose-production.yml"

if [ ! -f "$COMPOSE_FILE" ]; then
  echo "ERROR: $COMPOSE_FILE not found." >&2
  echo "This file is gitignored — recreate it locally with the prod env block before baking." >&2
  exit 1
fi

# Ensure the host-mount target exists so docker doesn't create it as root.
mkdir -p src/bakedknowledge

echo "→ Building bake image (--profile bake)..."
docker compose -f "$COMPOSE_FILE" --profile bake build rag-bake

echo "→ Running bake (~3-15 min depending on knowledge file count)..."
exit_code=0
docker compose -f "$COMPOSE_FILE" --profile bake up \
  --abort-on-container-exit \
  --exit-code-from rag-bake \
  rag-bake qdrant-bake || exit_code=$?

echo "→ Cleaning up bake services (other prod services untouched)..."
docker compose -f "$COMPOSE_FILE" stop rag-bake qdrant-bake 2>/dev/null || true
docker compose -f "$COMPOSE_FILE" rm -f rag-bake qdrant-bake 2>/dev/null || true

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
