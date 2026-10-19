# CLAUDE.md

Guidance for working on Poker Settle: an offline .NET MAUI app (Android + unpackaged Windows) for settling home poker cash games. No backend, no network calls; all data lives on the device.

## Commands

All scripts are Windows PowerShell 5.1-compatible; run them with `powershell -ExecutionPolicy Bypass -File <script>`.

| Task | Command |
|---|---|
| All tests (unit, FsCheck, migrations, perf, ViewModel integration) | `dotnet test tests\PokerSettle.Tests` or `scripts\test.ps1` |
| Build + run on Windows | `scripts\run-windows.ps1` (close the running app first: it locks `bin\Debug`) |
| Build Android (debug) | `dotnet build src\PokerSettle.App -f net10.0-android` |
| Build Windows only | `dotnet build src\PokerSettle.App -f net10.0-windows10.0.19041.0` |
| Signed release APK (+ signature, no-INTERNET and 16 KB checks) | `scripts\build-android-release.ps1` (keystore from `scripts\create-keystore.ps1`) |
| Self-contained Windows folder | `scripts\publish-windows.ps1` → `artifacts\windows\PokerSettle.exe` |
| Check native libs are 16 KB aligned | `scripts\check-16kb-alignment.ps1 <apk/aar/folder>` |

Debug-only environment variables (ignored in Release): `POKERSETTLE_DATA_DIR=<folder>` runs against a scratch data folder; `POKERSETTLE_DEBUG_LOG=1` forces debug logging on. Use them to run a second copy without touching real data (build with `-p:BaseOutputPath=<elsewhere>\` if the real app is running).

## Layout

```
src/PokerSettle.Core/      net10.0 library, no MAUI references (everything testable lives here)
  Models/                  records: Player, Game, GameSettings, Entry, BuyIn, Adjustment, Transfer, GameSnapshot
  Money/                   MoneyFormatter (culture-aware, signed), MoneyParser, Apportionment (largest remainder), GameMoney
  Settlement/              SettlementEngine (nets, discrepancy, fewest payments, banker, rounding, chips), ShareText
  Stats/                   board math, time filters, chart data
  Players/                 name cleanup/keys/close matches, suggestion ranking
  Data/                    DataStore (the ONLY database access), Migrations, Sql helper, DemoData
  Backup/                  JSON backup format (source-generated), BackupService, BackupCoordinator (UI flows)
  Logging/                 MEL provider, [LoggerMessage] catalog (Log.cs), Channel writer, rolling files
  Diagnostics/             CrashMarker, DebugReportBuilder
  Services/                platform-service interfaces, AppSettings (Preferences), SnackbarService, AppState
  ViewModels/              CommunityToolkit.Mvvm ViewModels for every screen
src/PokerSettle.App/       MAUI head: XAML pages, Shell, controls, MauiProgram (DI), platform service implementations
tests/PokerSettle.Tests/   xUnit + FsCheck against Core; Harness.cs wires ViewModels with fake platform services
scripts/                   build/run/publish/install scripts
```

## Conventions

- **Money is `long` minor units (paise).** Never `double`/`decimal` for stored amounts. "Game units" are paise, or whole chips in chip-mode games; `GameMoney` formats/parses either. Convert chips to money only via `SettlementEngine.MoneyNets` (largest remainder).
- **Format with `Settings.Formatter`** (configured culture + currency, default en-IN `₹1,25,000`), never `ToString()` with the device culture. Results use `FormatSigned` (`+₹1,250`, `−₹500`, `₹0`); in XAML show them with `controls:SignedLabel Sign="..."` so color and sign always go together. The formatter joins the sign to the amount with U+2060 (invisible); `ShareText` strips it.
- **All database access goes through `DataStore`.** It owns one connection; every public method is async, runs off the UI thread, and is serialized. Multi-row changes use `WriteAsync` (one transaction). Add SQL there, never in ViewModels.
- **Schema changes:** add a new entry to `Migrations.All` and bump `Migrations.Latest`. Never edit a shipped migration. Add a test that upgrades an old database (see `MigrationTests`).
- **Finalized results:** `Entry.Net` is written at finalization in the same transaction (cleared on reopen). The board is one GROUP BY over it; keep it that way (no per-player queries).
- **Logging:** add a `[LoggerMessage]` method to `Logging/Log.cs` and call it on an `ILogger` (`Log.SomethingHappened(...)` inside ViewModels, since `Log` is the logger property). Disabled logging must cost nothing, so no string interpolation at call sites. Never log phone numbers or UPI IDs (keys containing "phone"/"upi" are redacted as a last resort).
- **Commands:** every `[RelayCommand]` body goes through `RunSafeAsync`/`RunSafe` (logs, shows "Something went wrong. Your data is safe."). Expected domain errors (`DuplicatePlayerNameException`, `GameInProgressException`, `SettlementValidationException`, `GameNotEditableException`) are caught and shown inline.
- **Undo over confirmation:** reversible actions show `Snackbar.ShowUndo(...)`; native dialogs only for irreversible ones (delete player, import, clear all, reopen).
- **Refresh:** after a write, send `DataChangedMessage(area, gameId, source: this)` on `IMessenger`. Screens mark themselves stale and reload when they appear (or immediately when visible, ignoring their own messages).
- **XAML:** compiled bindings everywhere: `x:DataType` on every page and `DataTemplate` (binding warnings XC0022–XC0025 are build errors). Pages derive from `BasePage`. Long lists use `CollectionView` (never inside a ScrollView/StackLayout) with flat `Grid` templates; small fixed lists use `BindableLayout`. Make list rows tappable with a full-row `RowButton` (keyboard/screen-reader friendly), not a TapGestureRecognizer. Tap targets ≥ 48. Add `SemanticProperties.Description` to controls without visible text.
- **JSON:** only through source-generated contexts (`BackupJsonContext`, `LogJsonContext`); reflection-based serialization is disabled.
- **Never change** `<Company>` (Directory.Build.props), `<AssemblyName>`, `<ApplicationId>` or `<WindowsPackageType>None</WindowsPackageType>`: they decide where the data lives (`%LOCALAPPDATA%\PokerSettle\PokerSettle\Data` on Windows) and whether Android treats a build as the same app.
- Keep Core free of MAUI types; add a small interface in `Services/PlatformServices.cs` and implement it in `App/Services/PlatformServices.cs`.
