# The Airline Project on Linux (through Wine)

The game is a Windows WPF program, and WPF does not run natively on Linux. This package runs the
normal Windows build through Wine. It is the same game with the same save files, not a separate port.

## Install

1. Install Wine and winetricks (`sudo apt install wine64 winetricks`, `sudo dnf install wine winetricks`
   or `sudo pacman -S wine winetricks`). Wine 9 or newer is recommended.
2. Unpack `TheAirlineProject-Linux-Wine.tar.gz` and run `./install.sh` inside it.
3. Start **The Airline Project** from the menu, or run `~/.local/share/theairlineproject/game/theairline.sh`.

The first start creates a private Wine prefix (about a minute) and installs the core fonts.
Saved games and settings go to `Documents/theairlineproject` in your home folder.

## Status

This has not been tested on a real Linux system yet. Known risks:

- WPF under Wine uses software-heavy rendering. Big maps may be slow.
- Wine versions differ. If the window opens blank or the game stops at the splash screen,
  try a newer Wine, or Wine from winehq.org.
- The game writes `startup.log` to `Documents/theairlineproject`. Send it with any bug report.

Variables: `TAP_WINEPREFIX` sets another prefix folder, `WINE` selects another wine command,
`WINEDEBUG=err+all` shows Wine errors in the terminal.
