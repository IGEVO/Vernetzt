#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOCS_DIR="$ROOT_DIR/user-docs"
DOCS_OUTPUT_DIR="$DOCS_DIR/site"
WEBAPP_HELP_DIR="$ROOT_DIR/Igevo.Vernetzt.WebApp/wwwroot/help"

if ! command -v npm >/dev/null 2>&1; then
  echo "Error: npm is not available in PATH." >&2
  exit 1
fi

if [ ! -f "$DOCS_DIR/package.json" ]; then
  echo "Error: Missing $DOCS_DIR/package.json" >&2
  exit 1
fi

pushd "$DOCS_DIR" >/dev/null
npm ci
npm audit --audit-level=high
npm run build:ci
popd >/dev/null

if [ ! -f "$DOCS_OUTPUT_DIR/index.html" ]; then
  echo "Error: Help docs build did not generate $DOCS_OUTPUT_DIR/index.html" >&2
  exit 1
fi

rm -rf "$WEBAPP_HELP_DIR"
mkdir -p "$WEBAPP_HELP_DIR"
rsync -a --delete "$DOCS_OUTPUT_DIR/" "$WEBAPP_HELP_DIR/"

echo "Help documentation copied to $WEBAPP_HELP_DIR"
