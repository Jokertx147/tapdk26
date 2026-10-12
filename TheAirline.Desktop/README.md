# TheAirline.Desktop (Avalonia, Linux / macOS / Windows)

Minimal cross-platform UI: main menu, new game, load/save, and the airline screen with the clock, money, fleet and routes.
It uses `TheAirline.Core` (game logic) and `TheAirline.Presentation` (view models, no UI framework).

Requirements: .NET 10 SDK. From the repository folder:

    dotnet run --project TheAirline.Desktop

The first start loads the game data (a few seconds). Saves go to the `data/saves` folder next to the program.
Headless check of the view models without a window: `dotnet run --project TheAirline.Headless -- vm`.
