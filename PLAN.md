# Poker Settle — build plan

Checklist from the spec's phases. Ticked items are done, built for Android and Windows, and covered by passing tests where applicable.

## Phase 1 — Foundation
- [x] Environment check (dotnet --info, workloads, Android SDK, JDK)
- [x] Git repo with .NET .gitignore, global.json, central package versions
- [x] Solution scaffold: PokerSettle.Core, PokerSettle.App (MAUI: Android + Windows unpackaged), PokerSettle.Tests (xUnit + FsCheck)
- [x] DI in MauiProgram; platform service interfaces in Core with MAUI implementations
- [x] Data layer: SQLite (WAL, foreign keys, indexes), schema versioning via PRAGMA user_version, explicit migrations
- [x] Corruption recovery in the data layer (move aside with timestamp, start fresh) and pre-import snapshots (VACUUM INTO, keep 5)
- [x] Settings in Preferences
- [x] Debug logging (off by default): MEL provider, [LoggerMessage], Channel-based batched JSON-lines writer, rolling 3 × 1 MB
- [x] Logs screen and "Copy debug report"
- [x] Crash safety: safe command wrapper, global exception hooks (AppDomain, TaskScheduler, Android, WinUI), crash marker
- [x] Tests: migrations, corruption recovery, snapshots, logger (off writes nothing, rolling, redaction), crash marker

## Phase 2 — Game night
- [x] Money: paise math, parsing, culture-aware formatting (en-IN default), signed/colored results
- [x] Settlement engine: fewest payments, banker, rounding (largest remainder), chip mode, discrepancy options
- [x] Players: searchable list, add/edit, duplicate blocking, "Did you mean …?", archive/delete rules
- [x] New game: defaults, suggestions ranking, same-as-last-game, add-new-from-search, auto first buy-in, chip mode
- [x] Live game: cards, buy-ins (haptic), custom amounts, buy-in list with removal, undo snackbar, late players, early cash-out, keep screen on
- [x] Cash-out: live results, sticky footer, fill remaining, discrepancy resolution, validation
- [x] Review + finalize (nets stored on entries in the same transaction), mark paid, share/copy, reopen
- [x] Tests: money, settlement (unit + FsCheck properties), suggestions, data-layer game flows

## Phase 3 — History and board
- [ ] Games history: search, date filter, badges, detail, new game with same players/settings, delete with undo
- [ ] Board: one GROUP BY query, sortable columns, time filters, archived toggle, remembered sort/filter, table/cards adaptive
- [ ] Player page: stats, cumulative profit chart (GraphicsView), game-by-game history
- [ ] Tests: stats and time filters

## Phase 4 — Home, backup, polish
- [ ] Home: resume, new game, top 3, last 5, pending payments, backup reminder
- [ ] Recovery screen (offers backup import) and "closed unexpectedly" prompt on next launch
- [ ] Export (FileSaver), share backup, import with validation/summary/confirm/snapshot, clear all data
- [ ] Themes, app icon, splash, empty states, accessibility, adaptive layouts, window sizing
- [ ] Performance: 40 players / 2,000 games board < 100 ms test; demo database (Debug only)
- [ ] Tests: import/export round-trip and rejection; ViewModel integration test
- [ ] Scripts: run on Windows, signed release APK, self-contained Windows publish
- [ ] README and CLAUDE.md

## Phase 5 — Optional
- [ ] Merge duplicate players
- [ ] "Pay via UPI" deep link on Android (with "Copy UPI ID" fallback)
