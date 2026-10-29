# Architecture

Chosen framework: native .NET for Android, not MAUI/WebView. The existing Windows application is WPF/.NET with a platform-independent C# client and resolver. Native Android keeps these methods instead of porting them to a second language, while replacing desktop views with Android widgets and Media3.

- `src/Yoru.Core/Shared`: reused Firestore/Algolia API client, response models, media resolver, bounded/deduplicated API cache, startup metadata store and translation access. `CatalogApi.cs`, `ApiDataCache.cs`, `StartupCatalogStore.cs` and `Strings.cs` retain the Windows implementations. `Models.cs`/`PlaybackResolver.cs` only add propagated media MIME type so extensionless signed HLS/DASH URLs select the proper Media3 source.
- `AppState.cs`/`CatalogSession.cs`: cached Home with background refresh, shared API initialization, per-query paging/scroll state, deduplication, retry without skipping failed pages, bounded query-session cache. No server lists or resolved media URLs are persisted.
- `MobileStore.cs`: Android-specific local settings/library/history instead of Windows named mutexes. Atomic files, deletion markers and start-of-session suppression prevent stale saves from reintroducing deleted history. Mutations run off the UI thread; immutable published snapshots keep reads away from file-I/O locks.
- `MobileLayout.cs`: phone/tablet column/poster geometry shared with contract tests.
- `YoruApp.cs`/`MainActivity.cs`: Android storage/lifecycle/localized contexts, native bottom navigation, details and episode search, favorites/history, genres/seasonal/settings, RTL and immediate language recreation. No desktop window code is used.
- `FeedAdapter.cs`: one virtualized RecyclerView grid with full-span hero/section/history/episode rows, async diffing, cancellation/rebinding and responsive poster measurement. Cards are not all instantiated for a large catalog.
- `PosterCache.cs`: cancellation-aware network/decode workers, three download slots, sampled image decoding, 24 MiB memory and 128 MiB disk cache, fallback URLs and bounded response sizes.
- `PlayerActivity.cs`: Media3 native video surface and auto-hiding controller; an overlay header exposes server quality and player menu. Audio focus/noisy-headphone handling, streaming buffers, Android hardware decoding defaults, five-second progress checkpoints and pause/stop cleanup. Fresh server resolution supplies User-Agent/Referer; no HTML browser or remote scripts execute.

Android data resides in the app's private `files/yoru` directory; poster cache is under the Android cache directory. It is independent of `%LOCALAPPDATA%/YORU` on Windows. There is no cloud account/history sync or desktop-to-phone migration in this version.

Package `app.yoru.android`, version `1.0`/`90307`, min API 26 and target API 36. ARM64 and x86-64 are packaged. Release trimming/AOT are intentionally disabled in this first build to keep shared JSON/reflection/binding behavior reliable; device profiling should precede further startup/size optimization.


## Build 90313: downloads

`DownloadSources` inspects resolved public media and parses actual HLS variants; `EpisodeDownloader` streams one selected file/rendition into an atomic private archive. `DownloadLibrary` persists deduplicated jobs, attempt identity, progress and completed archive metadata. `DownloadService` owns the Android dataSync foreground lifecycle, queue, notifications, cancellation and source refresh. `DownloadPopup` reuses `PlayerGlass` for server/quality picking and management. Episode rows expose download controls; More contains Downloads. `PlayerActivity` handles local file requests before any network initialization and reuses Media3 and normal progress storage. Shared API/resolver sources and Windows remain unchanged. See DOWNLOADS.md for supported sources, exact limitations and tests.


## Build 90314

`ServerValidation` adds bounded streaming/download preflight and a small 45-second in-memory result/discovery cache. Details hands a `ValidatedServer` snapshot to Player; replacements validate before replacing the current Media3 session. `ServerGroups` and `ServerUi` provide API-driven quality headings. `DownloadGroups` builds per-anime immutable summaries, rendered by `DownloadAnimeCard`; unchanged-shape progress updates attached rows directly rather than rebuilding the page. Explore uses measured container width and Home's 160dp cell target with a minimum of three columns. See RESPONSIVE-SERVERS-UPDATE.md for details and limits.
