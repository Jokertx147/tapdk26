#!/usr/bin/env bash
# Installs the game for the current user: copies it to ~/.local/share and adds a menu entry.
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/theairlineproject/game"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"

mkdir -p "$DEST" "$APPS"
cp -a "$SRC"/. "$DEST"/
chmod +x "$DEST/theairline.sh"

cat > "$APPS/theairlineproject.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=The Airline Project
Comment=Airline management simulation
Exec=$DEST/theairline.sh
Path=$DEST
Icon=$DEST/TheAirline.ico
Terminal=false
Categories=Game;Simulation;
EOF

echo "Installed to $DEST"
echo "Start it from your application menu, or run: $DEST/theairline.sh"
