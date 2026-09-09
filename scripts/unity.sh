#!/usr/bin/env bash
set -euo pipefail
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -f "$PROJECT_ROOT/UserSettings/android-tools.env" ]]; then
  source "$PROJECT_ROOT/UserSettings/android-tools.env"
fi
EDITOR_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT_ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
UNITY_EDITOR="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$EDITOR_VERSION/Unity.app/Contents/MacOS/Unity}"
if [[ ! -x "$UNITY_EDITOR" ]]; then
  echo "Unity editor not found. Set UNITY_EDITOR to the Unity executable." >&2
  exit 1
fi
ACTION="${1:-validate}"
case "$ACTION" in
  setup) METHOD=GravityRoom.Editor.PhaseOneSetup.Configure ;;
  validate) METHOD=GravityRoom.Editor.PhaseOneSetup.Validate ;;
  build) METHOD=GravityRoom.Editor.PhaseOneSetup.BuildAndroid ;;
  setup2) METHOD=GravityRoom.Editor.PhaseTwoSetup.Configure ;;
  validate2) METHOD=GravityRoom.Editor.PhaseTwoSetup.Validate ;;
  build2) METHOD=GravityRoom.Editor.PhaseTwoSetup.BuildAndroid ;;
  release2) METHOD=GravityRoom.Editor.PhaseTwoSetup.BuildReleaseAndroid ;;
  *) echo "Usage: $0 {setup|validate|build|setup2|validate2|build2|release2}" >&2; exit 2 ;;
esac
mkdir -p "$PROJECT_ROOT/Logs"
exec "$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$PROJECT_ROOT" \
  -buildTarget Android -executeMethod "$METHOD" -logFile "$PROJECT_ROOT/Logs/$ACTION.log"
