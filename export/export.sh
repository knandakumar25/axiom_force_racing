#!/bin/sh
set -eu

case "${1:-help}" in
  help|-h|--help)
    echo 'After owner authorization: docker compose run --rm export <linux|windows|web|android|macos>'
    exit 0 ;;
  linux) profile=Linux; output=AxiomForceRacing.x86_64 ;;
  windows) profile=Windows; output=AxiomForceRacing.exe ;;
  web) profile=Web; output=site ;;
  android) profile=Android; output=AxiomForceRacing.aab ;;
  macos) profile=macOS; output=AxiomForceRacing.app ;;
  ios) echo 'iOS requires an approved native Mac workflow.' >&2; exit 2 ;;
  *) echo 'Select one supported target.' >&2; exit 2 ;;
esac

if [ "$#" -ne 1 ] || [ "${GITHUB_ACTIONS:-false}" = true ]; then
  echo 'Use a local invocation with exactly one target.' >&2
  exit 2
fi

project=/workspace/Game
profile_path="Assets/Settings/Build Profiles/$profile.asset"
if [ ! -f "$project/ProjectSettings/ProjectVersion.txt" ] || [ ! -f "$project/$profile_path" ]; then
  echo 'Create the Unity project and its target build profile before exporting.' >&2
  exit 2
fi

destination="/workspace/Builds/$1/$output"
mkdir -p "$(dirname "$destination")"
if [ "$1" = android ]; then
  set -- --android-export-type aab
else
  set --
fi
exec unity build "$project" --profile "$profile_path" --output "$destination" "$@"
