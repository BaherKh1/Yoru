# YORU V8 (0.8.0)

Branding:

- YORU replaces the previous visible name in both languages: window title, sidebar/header, player label, featured banner label, connection text, Settings/About and version. The native player title includes YORU plus the current anime/episode.
- YORU.exe, YORU.dll, product/title/company/description metadata and manifest identity. Internal namespaces, API project identifiers and provider request headers retain compatibility.
- Supplied transparent squircle artwork in header, Settings/About, player, EXE/window/taskbar and desktop shortcut. Nine ICO resolutions and PNG sizes: 16,20,24,32,40,48,64,128,256. Proportions use Uniform scaling.
- A YORU desktop shortcut is supplied for this build; the package includes a helper to recreate it after moving/extracting the app.

Compatibility:

- Central AppStorage migrates the legacy data root by atomic copies under migration/history mutexes. Original files are not deleted or moved. Existing destination data wins. Settings, favorites, history, progress/duration and deletion tombstones remain readable using existing serialization.
- Migration plans persist across interrupted copies, including partially copied history/tombstone pairs. Completion prevents old deleted entries from returning. Copy failures keep the original directory available and retry next launch. Decoder compatibility settings also carry over; old active-player markers and artwork cache files are not imported.
- Streaming/media resolution and command behavior are retained. No framework or backend rewrite; current V7 fixed-details layout and prior V6 performance/UI improvements remain intact.

Modified files:

- MainWindow.xaml, PlayerOverlay.xaml, PlayerWindow.cs: brand/logo/About UI and native player title only; existing poster/card/player-control design remains.
- AppIcons.cs, AppIdentity.cs, App.xaml.cs, app.manifest, AnimeWitcherDesktop.csproj: icon resources, shell identity, build metadata and YORU executable output.
- Localization/strings.json: English/Arabic brand values, About copy and V8 version.
- Assets/yoru-logo.png, yoru.ico, yoru-icon.png, yoru-*.png, build-icon.ps1: transparent cutout and standard Windows sizes.
- AppStorage.cs: centralized safe migration. HistoryStore.cs, LanguagePreferences.cs, Presentation.cs, ArtworkCache.cs, DecoderGuard.cs and PlaybackHealth.cs only change their default storage root.
- Create-Desktop-Shortcut.ps1, build.ps1: shortcut and complete branded distribution.
- IconDiagnostics.cs, V8Diagnostics.cs, tests/StorageMigrationChecks.cs, tests/Program.cs, tests/ApiChecks.csproj: branded EXE/icon assertions and migration tests; retained regressions still run.
- README.md, VALIDATION.md, BRANDING.md, PERFORMANCE.md: current release information and historical-performance context.

See evidence/v8-branding-checks.json, icon-checks.json and the retained design/player/cache/episode reports. The source-level audit in evidence/v8-preserved-code.json checks unchanged playback/backend/card code and permitted path/title-only changes.
