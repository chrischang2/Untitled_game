#!/usr/bin/env bash
# Usage: Tools/unity-batch.sh <Namespace.Class.Method> [extra unity args...]
# Runs Unity headless (with GPU, so rendering works) against this project and prints a filtered log.
set -u
UNITY="/c/Program Files/Unity/Hub/Editor/6000.0.68f1/Editor/Unity.exe"
PROJ="$(cd "$(dirname "$0")/.." && pwd -W)"
METHOD="$1"; shift
LOG="$PROJ/Captures/unity-$(echo "$METHOD" | sed 's/.*\.//').log"
mkdir -p "$PROJ/Captures"
"$UNITY" -batchmode -projectPath "$PROJ" -executeMethod "$METHOD" -logFile "$LOG" -quit "$@"
CODE=$?
echo "exit=$CODE log=$LOG"
grep -E "error CS|\[(ProjectSetup|SceneBuilder|Capture|Build|Automation)\]|Exception|Aborting batchmode|Compilation failed|Scripts have compiler errors" "$LOG" | grep -v "^UnityEngine\." | head -60
exit $CODE
