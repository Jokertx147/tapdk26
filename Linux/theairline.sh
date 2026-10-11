#!/usr/bin/env bash
# Starts The Airline Project (the Windows build) through Wine.
# Settings (optional environment variables):
#   TAP_WINEPREFIX  where the private Wine prefix lives (default ~/.local/share/theairlineproject/prefix)
#   WINE            the wine command to use (default: wine64 or wine)
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXE="$DIR/TheAirline.exe"

if [[ ! -f "$EXE" ]]; then
  echo "TheAirline.exe was not found next to this script ($DIR)." >&2
  exit 1
fi

WINE_BIN="${WINE:-}"
if [[ -z "$WINE_BIN" ]]; then
  if command -v wine64 >/dev/null 2>&1; then WINE_BIN=wine64
  elif command -v wine >/dev/null 2>&1; then WINE_BIN=wine
  else
    echo "Wine is not installed. Install it first, for example:" >&2
    echo "  Debian/Ubuntu: sudo apt install wine64 winetricks" >&2
    echo "  Fedora:        sudo dnf install wine winetricks" >&2
    echo "  Arch:          sudo pacman -S wine winetricks" >&2
    exit 1
  fi
fi

export WINEPREFIX="${TAP_WINEPREFIX:-${XDG_DATA_HOME:-$HOME/.local/share}/theairlineproject/prefix}"
export WINEARCH=win64
export WINEDEBUG="${WINEDEBUG:--all}"
# The game ships its own .NET runtime, so Wine does not need Mono or Gecko.
export WINEDLLOVERRIDES="${WINEDLLOVERRIDES:-mscoree,mshtml=d}"

mkdir -p "$WINEPREFIX"

if [[ ! -f "$WINEPREFIX/.tap-initialised" ]]; then
  echo "First start: creating the Wine prefix in $WINEPREFIX (this takes a minute)..."
  "$WINE_BIN" wineboot --init >/dev/null 2>&1 || true
  if command -v winetricks >/dev/null 2>&1; then
    # WPF text needs these fonts, and the game uses Segoe-like sans fonts.
    winetricks -q corefonts >/dev/null 2>&1 || echo "winetricks corefonts failed; text may look wrong."
  else
    echo "winetricks is not installed; install it and the 'corefonts' package for correct text."
  fi
  touch "$WINEPREFIX/.tap-initialised"
fi

cd "$DIR"
exec "$WINE_BIN" "$EXE" "$@"
