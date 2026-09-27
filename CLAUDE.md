# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

A **Bullshark Aero (BSA) fork of ArduPilot Mission Planner** — a C# WinForms ground control station for ArduPilot vehicles. Most of the fork's own work lives in the `BSA/` directory (preflight checklist wizard, config export/import, operational lock), reached through small integration hooks in `MainV2.cs`, `GCSViews/` and `ExtLibs/Controls/`. It is no longer the only place, though: upstream files now carry fork *behaviour* too — the VTOL-only mode list, QuickView appearance and scaling, the extended warning manager, BSA parameter metadata and branding. See "Fork changes inside upstream code" below; those are the files an upstream merge will fight over.

`.claude/` and this file are **version-controlled deliberately** — the `.gitignore` lines that hid them were removed so the `mission-planner-expert` agent below ships with the fork rather than living on one machine. The exception is `.claude/settings.local.json`, which stays **gitignored**: it is one developer's permission allowlist, full of machine-specific absolute paths, and is not project policy. Keep it that way — nothing checked in here should carry a personal path or identify the machine it was written on.

## Generated documents

Plans, changelogs, explainers and test-evidence PDFs are **written outside this checkout**, straight to their destination — never produced at the repo root and moved afterwards. Nothing at the repo root should be a generated document: a file sitting there is untracked, so it is not recoverable from git history, yet it *can* be swept up by `git add -A` or thrown away by a "discard all changes" in an SCM panel. An evidence PDF was lost exactly that way once.

Keep evidence filenames flat and descriptive — `BSA_<Area>_<Subject>_Test_Evidence.pdf`.

## The mission-planner-expert agent

This repo defines a **`mission-planner-expert`** subagent (in `.claude/agents/`) — a Mission Planner codebase expert with a pilot's operational mindset. Use it for substantive work here: architecture and UI-flow questions, MAVLink/`MAVLinkInterface` work, mission upload logic, telemetry/`CurrentState`, parameter and vehicle configuration, debugging, and especially for reviewing changes that touch flight-safety-critical paths (arming, failsafes, parameter writes, mission upload). Trivial lookups don't need it.

It carries a **repository map with file-level anchors**. When the structure moves, update the map in the same commit.

## Build, test, run

MSBuild (not `dotnet build`) is the established workflow. With a default Visual Studio 18 Community install:

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$vstest  = "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"

# Build the app (Debug)
& $msbuild MissionPlanner.csproj /restore /t:Build /p:Configuration=Debug /m

# Run it
& "bin\Debug\net461\MissionPlanner.exe"

# Build + run tests (MSTest)
& $msbuild MissionPlannerTests\MissionPlannerTests.csproj /restore /t:Build /p:Configuration=Debug /m
& $vstest "MissionPlannerTests\bin\Debug\net472\MissionPlannerTests.dll" /TestCaseFilter:"FullyQualifiedName~MissionPlanner.BSA.Tests"

# Single test
& $vstest "MissionPlannerTests\bin\Debug\net472\MissionPlannerTests.dll" /TestCaseFilter:"FullyQualifiedName~BsaHashTests.SameLogicalObject_DifferentKeyOrder_SameHash"
```

Quirks to know:

- Build the **project, not the solution**. `MissionPlanner.sln` references two class libraries under the uninitialised `ExtLibs/mono` submodule and dies with MSB3202; the Windows exe needs nothing from them.
- **Never `/t:Rebuild`.** Clean deletes the NuGet-generated `GdalConfiguration.cs` (it lives under `ExtLibs\GDAL\obj\<cfg>\net472\NuGet\`) and the build then fails with `CS0103: The name 'GdalConfiguration' does not exist`. Use `/t:Build` — deleting `bin\<cfg>` by hand first is fine and still produces a full tree.
- Output goes to `bin\Debug\net461\` even though the project targets net472 (`OutputPath` is hardcoded; `AppendTargetFrameworkToOutputPath` is off). Don't "fix" it — packaging depends on the path.
- If a build fails with **NETSDK1005** (missing restore assets), re-run `/t:Restore` on the specific project being built — the root projects use separate intermediate dirs (`obj3/` for the exe, `obj2/` for the lib) but restores can still stomp each other.
- `MissionPlannerTests.csproj` sets **both** `AutoGenerateBindingRedirects` and `GenerateBindingRedirectsOutputType` — a library project emits no `.dll.config` unless both are on, and without it the test host runs with *no* binding redirects: anything touching `ParameterMetaDataRepository` dies on a `Microsoft.Extensions.Options` version mismatch before it reaches the assertion. Don't remove them (same class of trap as the exe's redirect below).
- `MissionPlannerLib.csproj` (netstandard2.0, Xamarin/Android target) has **hundreds of pre-existing errors** in vendored projects — noise, not a regression signal. It also excludes `BSA/` entirely (BSA is WinForms-only). Judge changes by whether `MissionPlanner.csproj` and the tests build.
- Run `git submodule update --init` after a fresh clone.
- New source files under existing folders are picked up automatically (SDK-style globbing), but non-code content (e.g. JSON shipped to the output dir) needs an explicit `Content` entry in `MissionPlanner.csproj`, and BSA additions must stay excluded from `MissionPlannerLib.csproj`.
- Sources are **CRLF**, and most files under `ExtLibs/` carry a **UTF-8 BOM** while the root ones do not. Scripted edits must preserve both, per file — adding a BOM drags line 1 into the diff and pollutes blame.

### Shipping a build

`& $msbuild MissionPlanner.csproj /restore /t:Build /p:Configuration=Release /m` → `bin\Release\net461\`. `plugins\` are runtime-compiled `.cs` content copied by the csproj, so there is no separate plugin build — **except** the fork's own Judicar 2600 Lights plugin (`BSA/Plugins/`), which the exe build excludes. Bundles only carry precompiled DLLs, so build it after the exe with the same configuration — `& $msbuild BSA\Plugins\Judicar2600Lights.csproj /restore /t:Build /p:Configuration=Release` (it references `bin\$(Configuration)\net461\`) — and put `Judicar2600Lights.dll` in the exe's `plugins\` folder. Never leave both the `.dll` and the `.cs` there: Mission Planner loads both and the Actions tab gets two Lights buttons. **It is deliberately not built into or shipped with Mission Planner** (team decision, 2026-09-24) — it reaches a ground station through a Judicar 2600 bundle or by hand. Once installed, it shows its button only after the connected aircraft has sent Judicar 2600 named telemetry on the current link (`BSA/Telemetry/Judicar2600Identity`, the same field list as the health rules), because SERVO15/16 are lights only on that airframe.

`app.config` carries an explicit `System.Resources.Extensions` 4.0.0.0→6.0.0.0 binding redirect. The `.resx` resources are serialised against 4.0.0.0 but the shipped assembly is 6.0.0.0, and because it is only a *transitive* dependency `AutoGenerateBindingRedirects` does not reliably emit the redirect. Without it the exe throws `FileLoadException` on its first resource lookup (`Resources.get_mpdesktop` → `Program.Start`) and never opens a window. **Unit tests cannot see this** — smoke-test the actual exe, and confirm the redirect reached `bin\<cfg>\net461\MissionPlanner.exe.config` (expect **21** `<dependentAssembly>` entries; 20 means it is missing).

**`build.bat` is upstream's, not ours** — it builds an appx, code-signs as "michael oborne" and rsyncs to `mega.ardupilot.org`. Never run it as-is for a BSA release.

## Live testing (SITL and the UI harness)

- **SITL** is the main end-to-end harness. When MAVProxy is in the loop it holds port `5760`, so connect Mission Planner to **`tcp:127.0.0.1:5762`**.
- **`-config <file>.xml` isolates writes, not reads.** `Settings.FileName` is not assigned until `MainV2.cs:651`, by which point the `Settings` singleton has already loaded the operator's real config. A key *absent* from the test config is therefore inherited from the real one, so a test config must write every key it depends on rather than omit it.
- **UI automation:** UIAutomation cannot see this app's content — drive it with Win32 `EnumChildWindows` and verify from **screenshots**. `ToolStrip` items have no HWND (navigate them by keyboard), `GetWindowText` does not reflect a programmatic `TextBox.Text` assignment, and colour changes are only provable by sampling pixels.

## Architecture

### Assembly boundary — the most important structural fact

`MissionPlanner.csproj` (the WinForms exe) is where `MainV2.cs`, `GCSViews/**`, `Controls/**`, and all `BSA/**` code compile. Everything under `ExtLibs/` builds into **separate assemblies** that the exe references **one-directionally**. In particular:

- `ExtLibs/ArduPilot/` → `MissionPlanner.ArduPilot.csproj` (netstandard2.0) — contains `MAVLinkInterface.cs` (vehicle comms/protocol) and `CurrentState`. Code there **cannot** `using MissionPlanner.BSA.*`.
- The established idiom for letting lower assemblies call into BSA/exe code: a **static delegate hook declared in the lower assembly, wired by a composition root at startup** — same pattern as the codebase's own `System.CustomMessageBox.ShowEvent`. Example: `BsaLockGate` (in ArduPilot assembly) wired by `MissionPlanner.BSA.Lock.BsaLockComposition.Initialize()` in `MainV2.cs`.

Other key ExtLibs assemblies: `MissionPlanner.Utilities` (`Settings`, param metadata), `MissionPlanner.Comms` (serial/TCP/UDP links), `MAVLink` (generated protocol definitions), `MissionPlanner.Controls`, `GMap.NET.*` (maps).

### The BSA layer (`BSA/`)

Fork-specific safety features, deliberately isolated and dependency-injected (only composition roots touch `MainV2`/`Settings` globals, which is why the test suite runs without UI or MAVLink):

- `BSA/Core/` — preflight run engine, aggregator (GO/NO-GO/WARNING/UNKNOWN), checklist loader, `BsaPreflightService` (process-wide status singleton consumed by the lock), `BsaPaths` (all on-disk locations), `BsaHash` (canonical-JSON SHA-256).
- `BSA/Checks/` — check evaluation: `IValueProvider` implementations (telemetry/param/MP-config), `IRegisteredCheck` registry (mission-sanity etc.), `BsaPreflightComposition` (entry point `StartDefaultRun()`).
- `BSA/Config/` — approved-config package export/compare/import with backup+restore. The package also carries the Warnings Manager's definitions (`mpconfig/warnings.xml`, sourced from `{user data dir}\warnings.xml`); an import writes that file **and** calls `WarningEngine.LoadConfig()` — without the reload the imported warnings stay invisible until a restart *and* one press of the Warnings Manager's Save rewrites the file from the stale in-memory list, silently undoing the import (`WarningsReloadContractTests` pins this).
- `BSA/Config/` (bundles) — a schema-2 bundle also carries a typed core profile (quick panel, telemetry bindings, health rules; all-or-nothing) and may carry **executable plugin DLLs**. `BsaBundleTransaction` stages every one of those targets, backs up what it replaces, and rolls the lot back if start-up verification fails; files the operator is expected to edit (`warnings.xml`) are recorded in the journal's `EditableTargets` and deliberately *not* hash-verified at restart. The same goes for quick-panel settings (every key `BsaQuickViewCodec.OwnsSetting` claims): they land in `EditableSettings`, so renaming or recolouring a view before the restart does not roll the import back. Rollback still restores them, and the already-installed check compares the live quick panel against the bundle, so re-import repairs a drifted one. The profile also carries Flight Data's field-keyed label memory (`quickViewLabel_<field>`) as its `Labels` map, and it is **merged**, never replaced: the bundle's name wins for each field it names, and labels for other fields stay the operator's. Field names are validated (`[A-Za-z0-9_]`, no `customfield` slots) because they become settings keys. Keys the profile owns (`BsaQuickViewCodec.OwnsSetting`) leave the settings subset **only when a profile carries them**: schema-1 packages, including every automatic pre-import backup, keep them in the subset, or Restore Previous would lose the quick panel. Likewise `ConfigCompareEngine.Compare(live, package, policy)` ignores them only for a package with a profile, so the approved-package preflight check passes on a schema-2 bundle and an operator's quick-panel edits never fail it. **Plugin signing was removed by team decision (2026-09-21)** — there is no trust store and no publisher check. What remains is the descriptor/payload consistency check (`BsaPluginDescriptorValidator`), a re-hash of the payload at install time, and an explicit operator opt-in for executable code, which is now the only gate on it.
- Export asks which aircraft the configuration is for, with nothing pre-selected (`BSA/UI/BundleProfileChoiceForm`). The choices come from `BsaBundleProfileCatalog`: an aircraft profile (today only Judicar 2600) writes a schema-2 bundle stamped with that aircraft's package ID; "No aircraft profile" writes a schema-1 package (settings including the quick panel, plus the BSA files) and is never stamped. A new aircraft is one new catalog entry. With an aircraft profile, Export first warns when a visible quick-panel view shows a `MAV_*` named value the profile does not declare as a supported binding (`BsaBundleProfileCatalog.UndeclaredNamedValueCells`): nothing checks the panel against the bindings, so the view would travel in the bundle and show nothing. The operator can go on or stop and clear the view. It then offers the plugin DLLs loaded this session from the exe's `plugins\` folder (`BsaPluginExport.Discover`, `BSA/UI/BundlePluginChoiceForm`, all unticked); each chosen DLL is written as `plugins/<file name>.dll` plus a hashed descriptor, and the plugin ID must be the DLL's file name because the installer writes `plugins\<PluginId>.dll`. Plugins compiled from `.cs` at start-up have no file and are never offered.
- `BSA/Telemetry/` — the Judicar health evaluator and service (see "Judicar health fields" below).
- `BSA/Lock/` — operational lock after preflight: wire-level gate + UI gates (`BSA/UI/LockGateUi`), policy loaders, audit log.
- `BSA/Reports/` + `BSA/UI/` — JSON+HTML report writer (JSON is authoritative, filenames append-only) and the WinForms wizard.
- `BSA/DefaultConfig/` — shipped JSON defaults (checklist, policies), copied to output dir.

Standing decisions: **JSON everywhere, never YAML**. Fail-closed validation (a run is never published GO until its report is durably written). BSA runtime files live under `{user data dir}\BSA\` (config/, reports/, audit/, backups/). Engineering Mode uses its own dedicated passphrase Settings key (`bsa_engineering_password`) — reuses `Password.cs` hash primitives but never its storage key.

### Fork changes inside upstream code

Not everything fork-specific fits behind the BSA boundary. The live ones, each with the invariant it rests on:

- **VTOL-only mode list** (`ExtLibs/ArduPilot/Common.cs`). `getCommandableModesList()` strips `NonVtolPlaneModes` — `{3, 4, 14, 16, 24}`, ArduPlane mode *numbers* because the metadata's names and casing move between releases — and feeds the **command** surfaces only: the Flight Data mode combo (`bindQuickModeList`), `Joystick/Joy_ChangeMode.cs`, and `MAVLinkInterface.setMode(..., string)`, the single funnel all of them reach (so plugins are covered too). `getModesList()` stays unfiltered because it also names *incoming* heartbeats — filter it and the HUD keeps showing the previous mode after an RC-switch, failsafe or `DO_SET_MODE` into a withheld one, i.e. the GCS lying about the aircraft. The filter applies to `Firmwares.ArduPlane` only (3 is Auto on Copter). New mode UI must pick the right list deliberately.
- **QuickView appearance and scaling** (`ExtLibs/Controls/QuickView.cs`, `GCSViews/FlightData.cs`). Per-view label/value colour, hide, and a field-keyed label memory, all persisted through `Settings`; the number's font size is re-derived from a fixed probe on **every** paint rather than carried from the last one (carrying it made the size depend on resize history and oscillate with digit count). `colourLocked` opts a view out of the theme's per-name colouring (`Utilities/ThemeManager.cs:1082`).
- **Judicar health fields** (`ExtLibs/ArduPilot/CurrentState.cs`). `J26_DATA_OK`, `J26_ESC_OK` and `J26_GPS_RED_OK` are written only by `BSA/Telemetry/JudicarHealthService` and default to **0 (not OK)**. Never give them a healthy default: if the service fails to start, the fields must fail closed so their warnings fire while armed. A start failure is also shown to the operator from `MainV2.OnLoad`.
- **Warning manager** — extensions in `Warnings/` plus the quickview colouring sweep at the bottom of `MainV2.cs`; exported/imported as part of the BSA config package (above).
- **Branding and parameter metadata** — `Program.cs`, `Properties/AssemblyInfo.cs`, `Splash.Designer.cs`, `mpdesktop*.ico/png`, `Resources/splashdark.jpg`; BSA custom params appended to `ParameterMetaDataBackup.xml`.

### Data locations at runtime

`Settings.GetUserDataDirectory()` resolves to `C:\Users\<user>\Documents\Mission Planner\` on Windows (per-user content, including `BSA\`); cross-user caches (maps, SRTM, param metadata) live in `C:\ProgramData\Mission Planner\`.

### WinForms traps this fork has actually hit

- **The theme runs *after* `Activate()`.** `MainSwitcher.ShowScreen` calls `IActivate.Activate()` and only then `ApplyTheme` (`ExtLibs/Controls/MainSwitcher.cs:169-173`), so anything a screen sets up on activation is repainted underneath it on every switch to that screen. `ThemeManager` assigns `QuickView.numberColor` by **control name** (`Utilities/ThemeManager.cs:1075`); `QuickView.colourLocked` exists to opt a view out of that.
- **`Settings.Save` silently drops bad keys.** A key that is empty or contains a space or any of `/ - : ; @ ! # $ %` is skipped with a `Debugger.Break()` and a console line (`ExtLibs/Utilities/Settings.cs:519-543`), and the whole loop sits inside a bare `catch`. Anything vehicle-supplied — a `NAMED_VALUE_FLOAT` name, say — must be sanitised before it becomes part of a settings key.
- **A bound control is not always bound.** A `QuickView` waiting on a `NAMED_VALUE_FLOAT` that never arrived has `DataBindings.Count == 0`. The warning sweep in `MainV2` runs inside a catch-all, so an unguarded `DataBindings[0]` there does not throw visibly — it silently stops every later warning from being evaluated.
- UI updates from `SerialReader` or any async context must marshal to the UI thread (`BeginInvoke`).

### Tests

`MissionPlannerTests/` (MSTest, net472) is in `MissionPlanner.sln`. BSA tests live in `MissionPlannerTests/BSA/` under namespace `MissionPlanner.BSA.Tests` — **447 tests across 48 files, all green (last full run 2026-09-27 on `feature-judicar-airspeed-display`); keep it that way**. Pre-existing upstream test files elsewhere in the project may not all pass; don't chase those.

Tests run against the real `Settings.Instance`, i.e. the machine's actual configuration. A test asserting that *no* record exists must purge the keys it cares about in `TestInitialize` and put them back in `TestCleanup` — otherwise it passes or fails depending on whose machine runs it.
