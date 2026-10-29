# Responsive Explore, server validation and grouped downloads — build 90314

Android-only update, application ID `app.yoru.android`, version name 9.3. The existing install, local settings, favorites, history and download files remain compatible. No Windows changes or framework/backend rewrite.

## Explore

Explore now uses the measured RecyclerView container width after safe-area padding. It shares Home's 160dp poster-cell sizing target but calculates `max(3, ceil(availableWidthDp / 160))` rather than fixing three columns or capping the total column count. This caps each cell at 160dp on wider displays. Posters retain their existing 2:3 ratio, spacing, rounded corners and compact two-line labels.

| Available width | Explore columns |
| --- | --- |
| 320–480dp | 3 |
| 540–600dp | 4 |
| 720–800dp | 5 |
| 1024dp | 7 |
| 1280dp | 8 |
| 1920dp | 12 |

Window/inset/configuration changes update the grid without catalog reloads. Home, My List, Genres and Seasonal keep their previous column calculations and card sizes.

## Server discovery and availability

- `ServerValidation` reuses existing API discovery and `PlaybackResolver`. It distinguishes streaming validation from download compatibility; a source can stream while being unsuitable for offline saving.
- Default selection validates the preferred server first, then tries remaining hosts with a maximum of two concurrent fallback checks. Individual checks have an 8-second deadline; fallback searches have a 45-second budget, plus up to 12 seconds for initial discovery. Cancellation interrupts the lookup, including uncooperative resolver tasks at the caller boundary.
- Video probes use HTTP byte ranges and bounded reads (4KiB for playback/media objects, the existing 16KiB direct-download inspection). Larger manifests may be fetched up to 2MB; full episodes are never transferred to test availability. HLS checks media segments as well as playlist structure. Streaming still permits ordinary AES HLS; downloads retain their encryption/DRM/live/byte-range/external-rendition restrictions. No keys or licenses are fetched by preflight.
- Discovery and successful checks are kept only in bounded, in-memory 45-second caches. Failed-source results expire after 3 seconds. Retry invalidates the episode's old discovery/results. Validated playback is handed from Details to Player without repeating the checks immediately.
- Download default selection must produce real quality options, a valid download plan and a compatible media header before it is selected. It skips sources that respond but cannot supply downloadable video. Failed explicit selections show **“Server unavailable. Please select another server.”** Genuine disconnected/unreachable-network conditions keep the separate connection message.
- Server/quality changes are guarded by cancellation and revision checks. A late quality callback cannot pair an old source with a newly selected server. The queued download uses a snapshot of the confirmed server/quality. Actual transfers continue to refresh and enforce that selected source independently.

Both player and download server pickers share `ServerUi.Groups`: actual API qualities are grouped and numerically sorted where applicable; server rows and the current quality heading use purple selected states. Named/unknown qualities remain honest labels instead of invented resolutions. Entries are not probed en masse merely to display the list.

## Watching episodes

- Details shows a cancellable glass loading dialog while finding a valid source. It launches `PlayerActivity` only after source validation succeeds.
- If all checks fail, Details remains visible and displays **No Servers Available** with the requested message, Retry and Cancel. Connection failures remain distinct.
- Server switches and previous/next episode navigation use the same preflight. The existing player/request/progress remain intact until the replacement source passes validation; a failed replacement can be cancelled while retaining working playback.
- Decoder/player errors invalidate the failed source and try other candidates, with an exclusion set to prevent retry loops. Native callback generations prevent released players from acting on a later episode.
- Pausing/backgrounding cancels pending lookups so an asynchronous result cannot start new playback after the activity has paused. Resume restarts a cancelled initial lookup or resumes the retained player.
- Preflight confirms media/container/manifest availability; it cannot guarantee device decoder support, future network availability or that an upstream link will remain valid. Runtime errors still have recovery/error handling.

## Episode download icon

The Details episode row's 48dp download target is physically on the far left in both English and Arabic. Episode information remains to its right, with locale-aware text direction. Row minimum height, total padding, spacing, corners and existing actions remain intact. Expanded Downloads episode menus retain their separate three-dot action button.

## Grouped Downloads cards

- One card per anime, with a 72×108dp cached poster on the left, title, completed count, total completed/currently transferring bytes, active count and failed/cancelled/interrupted summaries.
- Tap a card to expand its downloaded episode rows. Those show episode order/name, selected server/quality, byte size and status, with existing offline playback, retry/cancel and confirmed-delete actions.
- A thin purple card progress bar and percentage update in place. Progress uses each transfer's reported percentage, weighted by known totals where possible, so an approximate HLS size cannot replace actual segment progress. Unknown queued sizes remain indeterminate. The bar hides when no downloads are active.
- Steady-state progress replaces row snapshots and updates only attached cells: no page navigation, adapter reload, full DiffUtil cycle, catalog request or poster reload. Structural changes (adding/removing/expanding groups) use the existing asynchronous diff. Group/episode ordering stays stable; expanded IDs survive activity-state recreation.
- Native progress animations respect the system animation setting. Poster requests reuse the existing cache and are not restarted on every progress tick.

## Files and checks

Core: `MobileLayout.cs`, `ServerGroups.cs`, `ServerValidation.cs`, `DownloadGroups.cs`, `AppState.cs`, centralized EN/AR translations.

Android: `MainActivity.cs`, `FeedAdapter.cs`, `PlayerActivity.cs`, `DownloadPopup.cs`, `ServerUi.cs`, `DownloadAnimeCard.cs`, version metadata.

Tests: 19 portable core groups, including new `ServerValidationChecks` and `DownloadGroupChecks`. Cases include unavailable/default fallback, valid explicit preference, short-lived reuse, refresh/retry, negative network caching, cancellation/timeouts, bounded probes, false video MIME rejection, broken HLS segments, streaming-vs-download compatibility, real quality grouping, source/quality isolation, grouped counts/size/status/progress, and 320–2560dp sizing. Existing API/resolver/history/cache/scroll-position/download/archive tests still pass. Host offline decode checks remain available for the saved MP4, MPEG-TS HLS and fMP4 HLS fixtures.

Evidence is under `evidence/responsive-servers-*`. The APK uses the same development signing identity for in-place updates.

No Android device is connected. Native UI appearance, insets, actual device decoding, background notification interactions and rapid navigation require phone verification; portable/host tests are not device UI or FPS measurements.

## Phone regression checklist

1. Explore: verify three columns on a regular phone; rotate, resize split-screen or unfold; verify larger containers gain columns while posters stay bounded. Open a title and Back to confirm exact scroll restoration.
2. Details: confirm the download icon is physically left in EN/AR; open its popup, observe checking, and confirm a compatible default is selected. Try a dead server and verify the short unavailable message; test airplane mode separately for the connection message.
3. Compare both server pickers: quality headings and current server/quality highlight must match actual API entries. Switch repeatedly during lookups and dismiss/cancel to check stale callbacks.
4. Watch: test an episode with an unavailable first server, all-unavailable behavior, Retry/Cancel, working and failed server switches, previous/next boundaries, background/resume and rapid episode changes. Working playback should remain until its replacement validates.
5. Downloads: expand/collapse anime cards, watch progress while scrolling, complete/cancel/retry/delete episodes, verify size/count/status changes and disappearance of progress after completion, then relaunch and play a completed episode in airplane mode.
