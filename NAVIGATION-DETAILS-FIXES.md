# YORU Android V9.3 corrections — build 90303

## 1. Stable bottom navigation

The previous scroll callback hid `frost` while moving, then replaced its captured bitmap after returning to idle. This made the bar visibly change between states. The scroll callback no longer touches navigation. `EnsureBackdrop` initializes the existing resting frosted layer once per window and retains it across scrolling, page changes and pagination. Background colors, alpha, blur radius, gradient, outline, elevation, 25dp icons, label spacing and the purple selection animation are unchanged. Content continues to scroll behind the translucent surface.

## 2. Home launch position

RecyclerView could retain an anchor/focused child while the hero skeleton was replaced with loaded Home rows; there was no explicit initial top position. The shell now takes initial focus instead of an off-screen action/search field, and RecyclerView does not preserve a child's input focus during data updates. Initial Home commits explicitly anchor item 0 at offset 0, including the skeleton-to-content transition. This initial positioning stops when real content is committed, the user starts dragging, or navigation leaves Home. Returning within the same session keeps the existing catalog/navigation position behavior. Logo, search placement, hero height and status-bar styling are unchanged.

## 3. Details artwork isolation

The pooled hero deliberately retained its prior bitmap for featured-image crossfades, but that policy also applied when a different anime was bound to the details page. Details bindings now clear both image layers and cancel pending work when the anime/page identity changes. Details heroes have anime-specific row keys; an attached mismatched details image is cleared before asynchronous row updates. The new image fades in from alpha 0 over the existing dark background. Featured Home carousel crossfades remain intact. Canceled image callbacks cannot install the old image.

## 4. Episodes on repeated visits

`episodes = await State.Cache.Episodes(...)` retained the cache's mutable List. Calling `episodes.Clear()` on the next navigation cleared that shared cached value, so later visits returned zero episodes. MainActivity now uses `IReadOnlyList<Episode>` and discards references instead of clearing lists. Android's new bounded `EpisodeLibrary` stores owned, read-only snapshots per anime and immediately supplies them on reopening. Empty or failed refreshes retain the last known nonempty snapshot.

Metadata and episode requests update independently. A details request captures its generation and cancellation token before its first UI await; stale success/error/autoplay callbacks cannot mutate a different visit, including A → B → A. While episode count is unknown the heading says “Episodes”, with three dark episode skeletons, rather than incorrectly showing 0. Successful empty results can still report a genuine zero.

## Modified files

- `src/Yoru.Android/MainActivity.cs`: initial focus/scroll, navigation hook removal, cached read-only episodes, skeleton/count handling and request ownership guards.
- `src/Yoru.Android/FloatingNavigation.cs`: retained resting surface with no scroll/idle style mutations.
- `src/Yoru.Android/CinematicHero.cs`, `FeedAdapter.cs`: artwork identity/reset/fade, anime-specific heroes and episode skeletons.
- `src/Yoru.Core/EpisodeLibrary.cs` (new), `AppState.cs`: bounded immutable episode snapshots over the existing API cache.
- `src/Yoru.Core/Localization/strings.json`: EN/AR generic Episodes heading.
- `src/Yoru.Android/Yoru.Android.csproj`: versionCode 90303; package/version name/certificate unchanged.
- `tests/Yoru.Core.Checks/EpisodeLibraryChecks.cs` (new), `Program.cs`: real-cache revisit regression plus ownership, empty/error refresh, cancellation and eviction coverage.

No Windows source, streaming API/resolver, Media3 player, poster dimensions, theme palette, MobileStore or user storage path was changed. Install this APK over the existing app; do not uninstall or clear data.

## Verification

Release APK build, package/signature/ZIP checks and 12 portable core test groups are recorded in `evidence/navigation-details-fixes-*`. Episode tests use the actual ApiDataCache with fixture transport and explicitly attempt to clear a view snapshot, reopen A after B, deliver a late canceled response, and return empty/failed refreshes.

No Android device is connected and the local emulator remains unavailable. Native visual/touch behavior and actual launch/playback still require device verification; portable tests and successful compilation do not establish on-device UI results.

### Device regression checklist

1. Install as an update, force-stop and relaunch with a warm cache; repeat offline/cold-cache where possible. Home must begin at the very top of its image with logo/search visible. Resume from playback and navigate away/back to verify session positions still work.
2. On Home, Explore and History, drag, fling, stop, and load more results repeatedly. The navigation surface must not disappear, recolor or flash on either motion or idle. Check Arabic and landscape too.
3. Open A, go back, open B; repeat quickly on slow networking and with warm images. B must show its own image or the dark placeholder, never A. Repeat Home featured → details and A → B → A.
4. Open A, note its episode count, return and reopen it several times, including after opening B. The count/list must persist. On a first cold fetch show the generic heading and dark skeletons until completion. Try retry/offline, episode search, resume and server switching.
