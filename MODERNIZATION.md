# Modernization plan: .NET Framework 4.5.2 to current .NET (WPF)

Target: SDK-style projects on `net10.0-windows` (or `net8.0-windows` if the machine only has the 8 SDK), keeping WPF and the existing XAML. Windows only.

Status: **the project files are converted, but nothing has been built or compiled.** The session that produced this work had no .NET SDK and no NuGet access, so the first build on a Windows machine with the SDK is the real test. See "Progress" at the end for what changed and what to expect.

## Scope at a glance

| Item | Size |
| --- | --- |
| Projects | `TheAirline`, `TheAirline.Resources`, `TheAirline.Data`, `TheAirline.Tests`, `Installer` (WiX) |
| `TheAirline.csproj` | 323 explicit `<Compile>` items, 193 `Page`/`Resource`/`Content`/`EmbeddedResource` items |
| Packages | 60+ entries in `packages.config`, mostly 2016 pre-release EF Core, `Microsoft.Extensions.*` 1.0 and `System.*` 4.x facades |
| Checked-in binaries | `Resource/*.dll` (MetroChart, Unity, Xceed AvalonDock, Interactivity), `packages/` (208 tracked files), `.nuget/NuGet.exe` |

## Dependency map

| Old dependency | Where it is used | Replacement |
| --- | --- | --- |
| `System.*` 4.x facade packages, `Microsoft.Extensions.*` 1.0, `Microsoft.AspNetCore.*` 1.0 | Pulled in by EF Core 1.0 | Delete. They ship in the shared framework or come back transitively. |
| `Microsoft.EntityFrameworkCore` 1.0 + `EntityFrameworkCore.SqlServerCompact40` | `Db/AirlineContext.cs`, `Migrations/` (4 files) | Current EF Core with SQLite (`Microsoft.EntityFrameworkCore.Sqlite`). SQL CE is dead on modern .NET. Regenerate migrations. |
| `Microsoft.SqlServer.Compact` | Same | Remove. |
| `Prism.Core/Wpf/Mef` 6.1, MEF (`System.ComponentModel.Composition`) | 21 `.cs` files (`[Export]`/`[Import]`, `AirlineBootstrapper`) | Prism 9 (`Prism.DryIoc`) or plain `Microsoft.Extensions.DependencyInjection`. This is the largest structural change. |
| `CommonServiceLocator` | 1 file | Drop with the bootstrapper rewrite. |
| `EnterpriseLibrary.*` 6.0 | 1 file (`Infrastructure/Adapters/EnterpriseLoggerAdapter.cs`) | Remove. Route Prism logging to NLog or `Microsoft.Extensions.Logging`. |
| `NLog` 4.3 | 1 file | `NLog` current. Keep `NLog.config`. |
| `Extended.Wpf.Toolkit` 2.9 | 1 XAML, 1 `.cs` | Current `Extended.Wpf.Toolkit` (Xceed, community edition). |
| `Xceed.Wpf.AvalonDock*`, `Xceed.Wpf.DataGrid` loose DLLs | Referenced in the csproj, little or no direct use found | Remove the DLLs. Re-add `Dirkster.AvalonDock` only if a compile error shows it is needed. |
| `De.TorstenMandelkow.MetroChart.dll` (checked-in binary) | 4 XAML files | Replace with LiveCharts2 or OxyPlot and rework those 4 charts. |
| `WpfLocalizeExtension` 2.3.2 + `XAMLMarkupExtensions` | 7 XAML files (`xmlns:lex`) | `WpfLocalizeExtension` current (supports modern .NET). Check the xmlns URI. |
| `WPFTaskDialog` 1.7.1 | 3 `.cs` files, `Infrastructure/Services/TaskDialogService.cs` | `TaskDialog` from `Ookii.Dialogs.Wpf` or a plain `MessageBox`. |
| `MathNet.Numerics` 3.11 | No source usage found | Remove, or bump if a build error says it is needed. |
| `Microsoft.Expression.Interactions`, `System.Windows.Interactivity` (loose DLLs) | 2 XAML files (`PageSettings`, `PageStartData`) bind events to commands through the `expression/2010/interactivity` namespace | Done: `Microsoft.Xaml.Behaviors.Wpf`, with the namespace changed to `http://schemas.microsoft.com/xaml/behaviors`. |
| `Moq` 4.2, `Ninject` 3.2 (tests) | `TheAirline.Tests` | Current `Moq`, `xunit` or `NUnit` (match what the tests use today). |
| `BinaryFormatter` | `Helpers/SerializedLoadSaveHelpers.cs` (1 file, old save path) | Disabled by default since .NET 8 and removed from the runtime path in later versions. Retire it. See "Save files" below. |

## Save files

118 `.cs` files carry `[Serializable]`, and `GameObject` uses `SerializationInfo` with custom versioning attributes. Two save paths exist:

1. `LoadSaveHelpers` writes a `SaveObject` with `DataContractSerializer`. This path still works on modern .NET and is the one to keep.
2. `SerializedLoadSaveHelpers` uses `BinaryFormatter`. Treat it as legacy. Either drop support for those old saves or write a one-off converter on .NET Framework before migrating.

Decide early whether old `.sav` files must still load. If not, delete the `BinaryFormatter` path and keep the rest.

## Order of work

1. **Branch and baseline.** On a Windows machine, confirm the original still builds (VS 2022 with the .NET Framework 4.5.2 targeting pack, or just trust the AppVeyor history) so there is a known-good reference.
2. **Convert `TheAirline.Resources`, `TheAirline.Data`, `TheAirline.Tests`** to SDK-style. These are small. Resources and images become `Content` or `Resource` items with globs.
3. **Convert `TheAirline.csproj`.** Replace the 323 `Compile` and 193 resource items with SDK globs. Set `UseWPF`, `TargetFramework`, `ImplicitUsings` off at first, and `Nullable` off. Delete `packages.config`, the `packages/` folder, `.nuget/`, and the loose DLLs under `Resource/` and `TheAirline/`.
4. **Fix dependencies in this order:** logging (NLog, drop Enterprise Library), then Prism and MEF (the bootstrapper and the 21 `[Export]` files), then the EF Core and SQLite layer, then charts, localization and dialogs.
5. **Fix API breaks as the compiler reports them.** Likely areas are `System.Windows.Forms` and `System.Drawing` use (5 files), `AppDomain` and `WebClient` calls, and `Settings`/`app.config` access.
6. **Replace the installer.** The WiX 3.8 project needs WiX 4 or 5, or an MSIX or `dotnet publish` flow.
7. **Smoke test.** Start the game, create an airline, run a few game days, save, reload.

## Known risks

- `Parallel.ForEach` over all airlines mutates shared singletons (`GameObject`, static collections). It worked on .NET 4.5, but newer runtimes schedule threads differently, so watch for race conditions.
- Behavior of `DataContractSerializer` for the `SaveObject` type should be checked against an existing `.sav` file after the port.
- The checked-in `Data/Data/plugins/AirportCSVReader.dll` is a binary plugin of unknown origin. Rebuild it or remove it.
- No license file exists in the repository. Confirm terms with the project owners before publishing a fork.

## Progress (branch `sdk-style-conversion`, not yet built)

Done:

- `TheAirline`, `TheAirline.Data` and `TheAirline.Tests` are SDK-style projects on `net10.0-windows` with WPF. The old 1,700-line `TheAirline.csproj` is now about 220 lines. The 77 image resources and the 10 embedded language files keep their old names.
- `packages.config` files for the game and the tests are gone, replaced by `PackageReference` entries. The Moq and Ninject versions float within their major version, so pin them once a build works.
- `app.config` keeps only the user settings. The Entity Framework 6, Enterprise Library and binding redirect sections are removed.
- `AppSettings.BasePath` now uses the executable's folder instead of the working directory, so `dotnet run` finds the `data` folder. A build step copies `Data/Data` to `<output>/data` (the `saves` folder is skipped).
- `standardStyles.xaml` points at `PresentationFramework.Aero2` instead of `PresentationFramework.Aero`.

Corrections to the plan above:

- `TheAirline.Resources` and `Data/Data.csproj` are **not in the solution** and nothing references them, so they were left alone. They look like leftovers.
- `packages/` and `.nuget/` were **kept**. The WiX 3.8 installer still uses them. Remove them when the installer moves to WiX 4 or 5.
- Build the game with `dotnet build TheAirline\TheAirline.csproj`, not the `.sln`. The solution includes the old WiX installer, which `dotnet build` cannot build.
- Source files the old project never compiled are excluded: `GUIModel/PopUpsModel/` (a stale copy of `CustomControlsModel/PopUpWindowsModel/`) and `Migrations/201505231846321_Regions.cs` (old EF6 API).

Expected first-build errors, roughly in the order to fix them:

1. **Prism and MEF**: `AirlineBootstrapper.cs` uses `MefBootstrapper`, which does not exist in Prism 9. `EnterpriseLoggerAdapter.cs` uses Enterprise Library. Both need rewriting. The 7 view models, `CloseGameEvent.cs` and `MainWindow.xaml.cs` also use Prism and may need small namespace fixes.
2. **EF Core**: `Db/AirlineContext.cs` calls `UseSqlCe`, and the files in `Migrations/` use the 2016 preview API. Switch to `UseSqlite` and regenerate the migrations.
3. **Dialogs**: `TaskDialogService.cs` and 2 view models use `WPFTaskDialog`, which may not run on modern .NET. `Ookii.Dialogs.Wpf` is the usual replacement.
4. **Charts**: the checked-in MetroChart DLL is referenced as a .NET Framework binary. It may compile but fail at runtime. 4 XAML files use it.
5. **Package versions**: `Extended.Wpf.Toolkit`, `WpfLocalizeExtension` and `Prism.Wpf` use floating major versions chosen without access to NuGet. If one does not restore, pick the nearest published major version.

Not yet checked: that the old `.sav` save files still load, and that the XAML renders the same under the modern WPF theme.

First build on a Windows machine (.NET SDK 10.0.401): package restore succeeded, so the floating versions resolve. The XAML stage then failed on the old Blend `Interaction.Triggers` namespace in two pages, which is now fixed. `System.Configuration.ConfigurationManager` and `System.Drawing.Common` were removed from the project because the SDK already provides them. The C# compile errors listed above have not been seen yet.

Second build (first C# errors): the compiler reported three groups, now addressed but not yet re-built.

- **`System.Device.Location.GeoCoordinate`** (about 140 uses) lived in .NET Framework only. `Infrastructure/Compat/GeoCoordinate.cs` provides a small replacement in the same namespace with the same haversine distance (earth radius 6,376,500 m), so the existing `using` lines keep working. The game only uses `Latitude`, `Longitude` and `GetDistanceTo`. Check a few airport distances against the old build if exact numbers matter.
- **`MemoryCache`** in `Translator.cs`: added the `System.Runtime.Caching` package.
- **Prism 6 with MEF** is replaced by Prism 9 with DryIoc (`Prism.DryIoc`). `App` is now a `PrismApplication` that registers `AppState` as a singleton, the dialog service, and the eight start-menu pages under their old navigation names (`/PageStartMenu` and so on). The view models lost their MEF attributes, and views now resolve their view model from the container in the constructor, which also covers `new PageStartData()` in `PageNewGame`. `AirlineBootstrapper.cs` and `EnterpriseLoggerAdapter.cs` are deleted. `MainWindow` logs through NLog. Region types moved to `Prism.Navigation.Regions` in Prism 9, which was inferred from the compiler errors and has not been confirmed.

Still expected: EF Core and SQLite (`AirlineContext.cs`, `Migrations/`), `WPFTaskDialog`, and whatever the compiler shows next.

Third build: three errors, now addressed but not yet re-built.

- **`UseSqlCe`**: `AirlineContext` now uses SQLite (`Airline.db` in the Documents\theairlineproject folder). Nothing created the database before, so `AppState` calls `EnsureCreated()` and adds a default `Settings` row on first run (Language stays empty, so the language page shows first). The old SQL CE migrations are not used by this path and still need regenerating if migrations are wanted.
- **`BinaryFormatter`** (SYSLIB0011): `FileSerializer` in `SerializedLoadSaveHelpers.cs` now uses `DataContractSerializer` with every concrete `[Serializable]` type as a known type and object references preserved. Old binary `.sav` files no longer load. Saving and loading is untested at runtime.

First run: the app reached `MainWindow` and crashed on `Icon="TheAirline.ico"` ("Cannot locate resource"). The icon is now a WPF `Resource` item. Prism and DryIoc resolution of the shell works.

First window: the start-data page showed empty continent, region and difficulty lists. The old SQL CE file shipped pre-filled and the new SQLite file is empty. `Db/DatabaseSeeder.cs` now fills continents, regions (from the unused `Migrations/Seeds` classes) and the Easy/Normal/Hard difficulties on first run. If you already ran the app once, delete `Documents\theairlineproject\Airline.db` so it is recreated.

New Game page: the Next button had no command in the upstream code (the 2.0 rewrite there is unfinished), so it did nothing. It now saves the chosen options as a `Player` row and navigates to `PageAirlineData`, passing the player id as a navigation parameter. `IsNavigationTarget` no longer throws. `PageAirlineData` itself is still mostly commented-out upstream code.

Save editing: `Helpers/SaveEditor.cs` plus a command line switch in `App.OnStartup`. `TheAirline.exe --set-money <save name> <amount>` loads `Documents\theairlineproject\saves\<name>.sav` with the game's serializer, sets the player airline's money, keeps a `.bak` copy and writes the save back. Untested: the new-game flow does not produce a playable game yet, so no real save exists to try it on.

Porting the older Page-based UI (chosen scope): the upstream 2.0 rewrite never finished the new-game flow, and about 70 game screens are still old `Page` classes that navigate through `PageNavigator`.

- `MainWindow` now has a full-window `Frame` (`frmContent`, hidden until used) and implements `PageNavigator`'s `NavigateTo`, `NavigateBack`, `NavigateForward`, `ClearNavigator` and `CanGoBack` on it, as the pre-2.0 window did. `PageNavigator.MainWindow` is set again.
- `Infrastructure/GameDataLoader` runs the old static `Setup.SetupGame()` once (UI thread, because it builds images). It swallows exceptions and only logs them through NLog, so a half-loaded game is possible; check the NLog output if lists come back empty.
- `PageAirlineData` is now a real page: choose an airline (filtered by the continent, region and start year picked on the previous page), see its type (Passenger, Cargo or Mixed), edit the CEO, pick country, currency, home airport and time zone, then Create. Create runs `GameObjectHelpers.CreateGame` in the background and opens the old `PageAirline` in the Page host.
- The EF `Continent`/`Region` rows are matched to the old static ones by name. New-game defaults: start year = newest listed, 5 opponents, Normal difficulty.
- Not done: language handoff to the older `Translator`/`AppSettings` (it may stay on the default language), the Esc/exit key handling, and returning from the game to the start menu.

Game data load: the first run of `SetupGame` failed on `Convert.ToDateTime("05.22.1911 00:00:00")`. The data files use US month-first dates and the old code depended on the machine culture. `Infrastructure/DataParsing.ParseDate` now reads them with explicit formats, and `GameDataLoader` runs the load under en-US so decimals parse too. `Setup.SetupError` keeps the first exception instead of only logging it. Expect more data-format problems, one at a time.

Data loading problems found through `startup.log` (the old loaders catch and swallow their own errors, so each one is now also written there as `LOADER ERROR`):

- Enum names in the XML do not match the code: `Verysmall`, `Verylarge`, `Long_Haul_International` against `VerySmall`, `VeryLarge`, `LongHaulInternational`. All 49 `Enum.Parse` calls in `Setup.cs` now use `DataParsing.ParseEnum`, which ignores case, underscores and spaces.
- One airport has a stray character in its coordinates (`024Ã‚`). `DataParsing.ParseInt` keeps only the digits.
- Some aircraft configuration files write `seat="104"` where the code looks up `Seat`. `DataParsing.AttributeValue` ignores attribute case. Before this fix the first such file aborted loading of all later configuration files.
- `LoadMajorDestinations` crashed on airports that had failed to load; it now skips them.
- One aircraft type allows 4 classes but only 1 to 3 class layouts exist; the class count is clamped to 3 and falls back to the 1 class layout instead of crashing.
- `GameDataLoader` logs the counts it loaded, so a partly loaded game is visible. A healthy load should show far more than 56 airports.

First look at the in-game screens: the Staff (pilots) screen crashed with "A TwoWay or OneWayToSource binding cannot work on the read-only property 'Name'". `Run.Text` binds two-way by default and modern WPF throws when the source property is read-only. All 19 `<Run Text="{Binding ...}">` bindings in the XAML now say `Mode=OneWay`. Other controls that bind two-way by default (TextBox.Text, CheckBox.IsChecked, ComboBox.SelectedItem) to read-only properties will fail the same way; the fix is the same, add `Mode=OneWay`.
Also fixed: `Names.SetupNames` looped forever (it never read the next line), which froze game creation at the pilots step.
