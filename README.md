# Poker Settle

Track buy-ins and cash-outs at your home poker cash game, work out who pays whom, remember your players, and keep an all-time board of who's up and who's down.

It runs on your Android phone (used at the table) and on your Windows PC. There is no server, no account and no internet: **all data stays on the device**.

## What it does

- **Players**: searchable list with nickname, phone, UPI ID, notes and a colored avatar. Duplicate names are blocked (ignoring case and spaces) and close matches get a "Did you mean …?". Players with history can be archived (stats kept); players without history can be deleted. Duplicates can be merged.
- **New game in seconds**: title defaults to the date, buy-in comes from your last game, "Same players as last game", suggested players ranked by who you actually play with, and "Add 'Name' as a new player" right from the search box. Optional chip mode (e.g. 1,000 chips = ₹500).
- **Live game**: one card per player with a big **+ Buy-in** button (light haptic on the phone), custom amounts, tap the total to see and remove individual buy-ins, early cash-out, late players, and **Undo** for every action. The screen stays awake during a game.
- **Cash-out**: results update as you type; a sticky footer shows what's left to allocate until it says **Balanced ✓**, with "Fill remaining" when one amount is left. A count that doesn't balance is never finalized silently: fix it, split the difference equally, or assign it to one player (saved with the game).
- **Settlement**: fewest payments (default) or everyone through a banker; optional rounding to ₹1 or ₹10 that still adds up exactly. Mark payments paid, and share a clean summary (with UPI IDs) to WhatsApp or copy it.
- **Board**: net, won, lost, games, win rate, average, biggest win/loss, last played. Sort by any column and filter by time period. A table on wide windows, cards on the phone. Each player has a page with a cumulative profit chart.
- **History, Home and backups**: search past games, resume the game in progress, see pending payments by person, and export/import a JSON backup.

## Prerequisites (Windows PC)

- **.NET 10 SDK** (10.0.100 or newer) with the **.NET MAUI** workloads for Android and Windows.
  The easiest way is Visual Studio 2026 with the *.NET Multi-platform App UI development* workload. Without Visual Studio:
  `dotnet workload install maui-android maui-windows`
- **Android SDK** with API level 36 and build-tools 36 (installed with the MAUI workload/Visual Studio), and a **JDK 17 or 21** (`JAVA_HOME` set).
- **Git**, if you want to keep using the repository.

Check with `dotnet --info` and `dotnet workload list`. This machine was set up with .NET SDK 10.0.401, the android and maui-windows workloads, Android SDK platforms 35/36 and Temurin JDK 21.

All scripts below are run from the repository folder (`C:\Users\SkySun\source\repos\PokerSettle`) in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\<script>.ps1
```

## Run it on Windows

**While developing:** `scripts\run-windows.ps1` builds and starts the app. Close the app before running it again.

**As a normal program:** `scripts\publish-windows.ps1` creates `artifacts\windows\` with **PokerSettle.exe** and everything it needs (no .NET install required). Copy the folder wherever you like and pin a shortcut to the Start menu or taskbar. The first time, Windows SmartScreen may warn about an unknown publisher: choose *More info → Run anyway*.

The app runs *unpackaged* (a plain .exe), so it keeps its data in the same folder no matter where you put the program.

## Install it on your Android phone

### 1. Create your signing key (once, ever)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\create-keystore.ps1
```

This creates `%USERPROFILE%\.pokersettle\poker-settle.keystore` and asks for a password. Add `-GeneratePassword` to skip the prompts: a strong password is generated and saved as `keystore-password.txt` next to the keystore, and the build script reads it from there.

**Your key already exists:** it was created on 19 Oct 2026 with `-GeneratePassword`, so the folder `%USERPROFILE%\.pokersettle\` holds both `poker-settle.keystore` and `keystore-password.txt`. **Back up that whole folder** (e.g. to a USB drive or a password manager). Every future version must be signed with this same key, or your phone won't accept it as an update. Don't run `create-keystore.ps1` again. The keystore is never committed: `*.keystore`, `*.jks` and `signing/` are in `.gitignore`, and the folder is outside the repository anyway.

### 2. Build the APK

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-android-release.ps1
```

It takes the keystore password from `POKERSETTLE_KEYSTORE_PASSWORD`, else from `keystore-password.txt` next to the keystore, else asks. It builds `artifacts\PokerSettle-1.0.0.apk` (about 42 MB; runs on 64-bit and 32-bit ARM phones) and checks it: the signature, that it does **not** request internet access, and that its native libraries support 16 KB memory pages (needed on newer phones).

### 3. Put it on the phone (pick one)

**USB:** on the phone, enable *Developer options* (Settings → About phone → tap *Build number* 7 times), then turn on *USB debugging*. Connect the cable, accept the prompt on the phone, and run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-android.ps1
```

**Sideload:** copy the APK to the phone (USB, Google Drive, WhatsApp to yourself…), tap it in your file manager and allow *Install unknown apps* for that app when asked.

To update later, build and install a new APK the same way; your data is kept.

> ⚠️ **Read this before uninstalling or switching builds**
>
> - **Uninstalling the app deletes all its data on the phone.** Export a backup first (Settings → Export backup or Share backup).
> - **Switching between a debug build** (deployed from Visual Studio or `dotnet build -t:Run`) **and your release APK, or changing keystores, forces an uninstall**, because the signatures differ. Android then refuses the update (`INSTALL_FAILED_UPDATE_INCOMPATIBLE`), and the only way forward is to uninstall, which deletes the data. Export a backup first, then import it after reinstalling.
> - Android's cloud backup is switched off for this app so your data never leaves the phone, so a phone reset or switch doesn't bring it back automatically either. Use backups.

## Where your data lives

| | Location |
|---|---|
| Android | The app's private storage (`/data/data/com.pokersettle.app/files/`). Other apps, and you without root, can't read it. |
| Windows | `%LOCALAPPDATA%\PokerSettle\PokerSettle\Data\` |

Inside that folder:

- `poker-settle.db` (+ `-wal`, `-shm`): the SQLite database (WAL mode; every action is saved immediately, so closing the app or Android killing it mid-game loses nothing).
- `snapshots\`: automatic safety copies taken before every import or "clear all" (the newest 5 are kept).
- `logs\`: debug logs, only when you turn them on.
- `poker-settle.db.corrupt-<date>`: only if the database ever couldn't be opened (see Troubleshooting).

App settings (currency, theme, remembered sort and filters, debug logging) are kept separately: on Windows in `%LOCALAPPDATA%\PokerSettle\PokerSettle\Settings\preferences.dat`, on Android in the app's private preferences.

Settings → About shows the exact database path and size, the app version and the schema version.

## Backup and restore

- **Export backup** (Settings, or *Back up now* on Home) opens a save dialog for `poker-settle-backup-YYYY-MM-DD.json`.
- **Share backup** sends the same file through the share sheet: Google Drive, WhatsApp, email, …
- **Import backup** lets you pick a backup, checks it, shows what's in it ("14 players, 42 games") and only replaces your data after you confirm. A snapshot of your current data is saved first.
- Home shows a gentle reminder when games have finished since your last backup.
- **Moving between phone and PC:** export on one, import on the other. The backup also carries your currency, number format and game defaults.
- The file is readable JSON. Amounts are whole numbers in paise (1/100 of the currency), or whole chips for chip-mode games.

**Clear all data** (Settings) deletes everything after you type `DELETE`; a snapshot is kept in `snapshots\`.

## Debug logs

Off by default, and when off nothing is written to disk.

- Turn on: **Settings → Troubleshooting → Debug logging**.
- **View logs**: filter by level, search, tap an entry to see its details, *Copy all*, *Share log file*, *Clear*.
- **Copy debug report**: app and schema version, device, database size, settings, data counts and the last 200 log entries, ready to paste into Claude Code or an issue.
- Logs are JSON lines in the `logs` folder, at most 3 files of 1 MB. They never contain phone numbers or UPI IDs.
- If the app ever closes unexpectedly, the next launch offers to turn logging on and to export your data. Every button is also protected: errors show "Something went wrong. Your data is safe." instead of crashing.

## Troubleshooting

| Problem | What to do |
|---|---|
| Build says a workload is missing | `dotnet workload install maui-android maui-windows` (or add the MAUI workload in the Visual Studio Installer). |
| `run-windows.ps1` says the app is already running | Close Poker Settle (it locks its files while running) and run the script again. |
| `install-android.ps1` finds no phone | Use a data cable, enable USB debugging, accept the "Allow USB debugging?" prompt on the phone, and run `adb devices`. Some PCs need the phone maker's USB driver. |
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | The installed app was signed with a different key (e.g. a debug build). Export a backup in the app, uninstall it, install the APK, import the backup. |
| "We couldn't open your data" screen | The database file was damaged. It was moved aside (renamed `.corrupt-<date>`, never deleted) and a fresh one started. Import your latest backup from that screen or from Settings. |
| Amounts look wrong (symbol, commas) | Settings → Money: pick the currency and number format. The default is Indian rupees with Indian grouping (₹1,25,000). |
| A game won't finalize | Cash-outs must add up to the buy-ins. The footer shows the difference; fix it, or choose split/assign. Every player needs an amount (0 is fine). |
| Something else | Turn on debug logging, reproduce it, then Settings → Copy debug report. |

## Development

- `scripts\test.ps1` runs all tests: money, settlement (including FsCheck property tests), stats, suggestions, migrations, corruption recovery, logging, backup round-trip, a 2,000-game performance check and an end-to-end ViewModel test of a whole game night.
- Debug builds have **Settings → Developer → Use demo database**, a separate file with 40 players and 2,000 games (marked DEMO); your real data isn't touched.
- Architecture, layout and conventions are in [CLAUDE.md](CLAUDE.md). The build plan and progress are in [PLAN.md](PLAN.md).

## Decisions

Choices made where the spec left room:

1. **Project folder:** the session started in your home folder, so the app was first created in its own folder rather than turning the home folder into a git repository; it now lives with your other projects in `C:\Users\SkySun\source\repos\PokerSettle`.
2. **Versions:** .NET 10 (SDK 10.0.401), MAUI 10.0.110, CommunityToolkit.Maui 15.0.1, CommunityToolkit.Mvvm 8.4.2, Microsoft.Data.Sqlite 10.0.13 (SQLitePCLRaw 2.1.13, whose Android libraries are 16 KB-aligned; verified), xUnit 2.9.3 and FsCheck 3.5.0. Versions are pinned centrally in `Directory.Packages.props`.
3. **SQLite access:** Microsoft.Data.Sqlite with hand-written SQL instead of an ORM. It uses no reflection (safe for trimmed release builds) and gives full control of WAL, foreign keys, transactions, `quick_check` and `VACUUM INTO`. One long-lived connection, serialized; `synchronous=FULL` so even a phone dying mid-game keeps every committed action.
4. **Schema history:** version 1 (tables) and version 2 (store each player's net at finalization, plus board indexes), so the migration path is exercised and tested from day one. Ids are `AUTOINCREMENT`, so Undo of a deleted game can restore its original ids.
5. **Names:** uniqueness ignores case and all whitespace ("Amit K" = "amitk"). Close matches use edit distance, prefixes and first names.
6. **Results vs payments:** the stored net (and the board) is the exact result. Rounding changes only the payment amounts; the review shows "settles as …" when they differ.
7. **Discrepancies:** adjustments are stored per player in game units (chips in chip mode). When splitting, leftover paise go to the earliest seats.
8. **Fewest payments** looks for exact opposite pairs at every step, not only the first, then pays biggest loser to biggest winner. Ties break on the lower player id, so results never change.
9. **New game defaults:** buy-in and chip mode come from your last game; settlement mode and rounding come from Settings. "New game with same players and settings" copies everything, including the banker if they're playing. A game needs at least 2 players to start.
10. **Banker mode:** if no banker is set, the first seat (usually the host) is preselected.
11. **Late players** get one default buy-in when the game was started with "Everyone starts with 1 buy-in". Players can be removed from a live game (with Undo).
12. **Reopen:** clears results and adjustments; payments are recalculated at the next finalize, and paid marks are kept for payments that come out identical.
13. **Import:** replaces all data (snapshot first), recomputes nets from the rows, and restores currency, number format and game defaults (not theme or logging). "Share backup" also counts as a backup for the reminder.
14. **Privacy:** Android cloud backup is disabled (`allowBackup=false`); the release APK has no INTERNET permission (checked by the build script).
15. **Crash detection:** an empty marker file exists only while the app is in the foreground; if it's still there at launch, the last session ended unexpectedly.
16. **Snackbar** is an in-app control placed on every page through a shared page template; Undo stays available for 7 seconds.
17. **Line breaks:** an invisible word joiner keeps the sign and amount together ("−₹500" never splits across lines). It is stripped from shared/copied text.
18. **Currencies:** a fixed list (₹, $, €, £, A$, C$, S$, AED); all use 2-decimal minor units. "Pay via UPI" appears only for ₹ on Android, with "Copy UPI ID" always available because some UPI apps reject payment links.
19. **Accessibility:** list rows are full-row buttons (not tap gestures), so they work with the keyboard on Windows and with screen readers; amounts never rely on color alone.
20. **Do not change** `<Company>`, `<AssemblyName>`, `<ApplicationId>` or `<WindowsPackageType>`: they determine where the Windows data lives and whether Android treats a build as the same app.
21. **Developer switches** (Debug builds only): `POKERSETTLE_DATA_DIR` and `POKERSETTLE_DEBUG_LOG` let a second copy run against scratch data.
22. **Android minimum** is Android 6.0 (API 23). The release APK includes 64-bit ARM, 32-bit ARM (budget phones on 32-bit Android Go) and x86_64 (emulators, Chromebooks).
