# YORU Android V1.0 — cinematic interface (90302)

The visual reference was a provided design mockup image: an edge-to-edge anime image beneath the status bar, small branding/search above, and readable title/actions over the lower gradient. This implementation keeps YORU's #090C14 background and #8B5CF6/#A78BFA accents.

## Implemented

- A full-bleed Home hero, approximately 53% of portrait height, with transparent status bar, safe-area branding/search, bottom gradient, original anime title and genres, Watch Now and persistent My List actions. Swipe horizontally through the first five real Home results; accessibility scroll actions provide the same navigation. Artwork crossfades over 220ms without clearing the previous image. Short landscape windows and larger text get additional height to prevent overlapping layers.
- Floating bottom navigation with consistent 25dp drawn icons, 48dp+ touch targets, labels, ripple feedback, one persistent 225ms sliding indicator, and icon/label color transitions. The indicator uses the cells' physical positions, including RTL, and retargets from its current translation during rapid navigation.
- Real frosted navigation backdrop: Android 12+ uses RenderEffect; older Android versions use a three-pass, separable blur of a one-fifth-size capture on a worker thread. Captures happen after scrolling settles, with generation checks to discard stale work. During scrolling the translucent background/gradient stays visible without blur captures. The bitmap is reused until dimensions change and released on destruction.
- Content scrolls behind navigation. Bottom padding accounts for its full height, system bars and scaled labels; keyboard handling keeps search results accessible. Top/sides account for status bars and cutouts.
- Virtualized 2:3 posters, rounded artwork, balanced two-line titles and metadata, appropriately sampled image requests, height-aware memory-cache keys and a cache recheck after a download slot becomes available. Static hero/poster skeletons avoid one animator per card. Existing paging, cancellation, diffing and bounded image caches remain.
- More uses compact icon/label/chevron rows, grouped corners, subtle dividers and translated section labels. Settings, favorites, genres, seasonal browsing, history and playback retain their existing actions.
- Navigation avoids fetching another catalog page on every return; near-end/underfilled viewports trigger paging instead. Stale asynchronous feed updates cannot restore an earlier section's scroll position.
- English/Arabic labels and RTL remain supported. The YORU wordmark and search retain the requested top-left/top-right branding positions in either language; navigation order, metadata and content follow locale direction.

## Files

Android UI: `MainActivity.cs`, `FeedAdapter.cs`, `Ui.cs`, `Resources/values/styles.xml`; new reusable `CinematicHero.cs`, `FloatingNavigation.cs`, `StreamIcons.cs`. Image loading: `PosterCache.cs`. Android-only core helpers: `MobileLayout.cs`, new `BackdropBlur.cs`, and `Localization/strings.json`. Package version code: `Yoru.Android.csproj`. Contract/algorithm checks: `tests/Yoru.Core.Checks/Program.cs`.

The copied API/resolver, Media3 player, MobileStore, storage paths, package identifier and signing certificate are unchanged. The previous startup compatibility setting (`AndroidEnableMarshalMethods=false`) remains enabled. Version code increases to 90302 so this can update existing installations without uninstalling or deleting data. Windows sources are not modified.

## Verification and limits

- Release APK compilation/signing and package checks are recorded in `evidence/cinematic-*`.
- Eleven portable test groups pass: existing API/resolver/cache/history/favorites/language/paging checks plus cinematic geometry and blur correctness. Geometry covers 320–1280dp phones/tablets, portrait/landscape, bottom insets and 100–200% font scales. These are layout contracts, not Android screenshots or touch tests.
- The 200×44 blur fixture averaged 0.57ms on this Windows host. This is not phone frame-rate, memory, or GPU profiling.
- No connected Android device was available and the local emulator cannot boot with the available setup. Actual rendered layouts, rapid taps, touch scrolling, system-inset behavior and streaming on hardware remain unverified. See the manual checklist in DEVICE-TESTS.md. No device-runtime or FPS claim is made.

Build with the existing `build.ps1`; output remains `artifacts/YORU-Android-V1.0.apk`. A separately named delivery copy is produced outside the repository.

## Subsequent corrections

Build 90303 supersedes the scroll/idle backdrop policy described above: the existing resting glass surface is now retained across scrolling without visibility or capture changes. See NAVIGATION-DETAILS-FIXES.md for all four corrections and current tests.
