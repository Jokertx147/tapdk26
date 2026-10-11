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
