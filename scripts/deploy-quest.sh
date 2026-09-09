#!/usr/bin/env bash
set -euo pipefail
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APK="${1:-$PROJECT_ROOT/Builds/Android/GravityRoom-Phase1.apk}"
ADB="${ADB:-adb}"
PACKAGE_ID="com.espitman.gravityroom"

if [[ ! -f "$APK" ]]; then
  echo "APK missing: $APK. Run scripts/unity.sh build first." >&2
  exit 1
fi
if [[ -n "${QUEST_SERIAL:-}" ]]; then
  TARGET=( -s "$QUEST_SERIAL" )
else
  DEVICES=( $("$ADB" devices | awk '$2 == "device" {print $1}') )
  if [[ "${#DEVICES[@]}" -ne 1 ]]; then
    echo "Connect and authorize one Quest, or set QUEST_SERIAL to select the target." >&2
    "$ADB" devices -l
    exit 1
  fi
  TARGET=( -s "${DEVICES[0]}" )
fi
MODEL="$("$ADB" "${TARGET[@]}" shell getprop ro.product.model | tr -d '\r')"
if [[ "$MODEL" != Quest* ]]; then
  echo "Selected device is not a Quest: $MODEL" >&2
  exit 1
fi
"$ADB" "${TARGET[@]}" install -r "$APK"
ACTIVITY="$("$ADB" "${TARGET[@]}" shell cmd package resolve-activity --brief "$PACKAGE_ID" | tr -d '\r' | tail -n 1)"
if [[ "$ACTIVITY" != "$PACKAGE_ID/"* ]]; then
  echo "Could not resolve launch activity for $PACKAGE_ID: $ACTIVITY" >&2
  exit 1
fi
LAUNCH_OUTPUT="$("$ADB" "${TARGET[@]}" shell am start -W -n "$ACTIVITY")"
echo "$LAUNCH_OUTPUT"
if echo "$LAUNCH_OUTPUT" | grep -Eq "(Error:|Status: error)"; then
  echo "Failed to start activity $ACTIVITY on Quest." >&2
  exit 1
fi
