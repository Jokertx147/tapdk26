# TheAirline.Core

The game logic of The Airline Project, with no user interface code: models, helpers, data loading, saving and the game clock.
It targets plain `net10.0`, so it builds and runs on Windows, Linux and macOS.

- `TheAirline` (WPF) references this project and connects to it through `Infrastructure/UserInterface.cs`, a handful of hooks
  (show a message, choose a homebase, open the airline page, build the map image, show a busy cursor). A hook that is not set is
  simply skipped, which is how `TheAirline.Headless` runs the game without a window.
- `TheAirline.Headless` loads the data, creates a game and simulates 40 days. Run it from its folder with `dotnet run`.
- Saves made before this split name their types in the old `TheAirline` assembly; the save loader falls back to this assembly.
- Paths are built with `/`, which Windows accepts too. File names in the `Data` folder are case sensitive on Linux.

Not done yet for a native Linux game: a Linux user interface (for example Avalonia), and images/logos stored as Windows paths
inside old save files.
