# Explore grid and player glass update — build 90312

Android-only update, package `app.yoru.android`, version 9.3. Install over the existing app to retain local settings, favorites and playback progress.

## Explore

- Exactly three columns in Explore, including search/filter/sort results and skeletons. Other pages retain their previous responsive column counts.
- Native RecyclerView virtualization, paging, image caching and exact Back-position bookmarks remain in place.
- Posters keep their 2:3 aspect ratio and original network/cache sources. Compact horizontal margins and 12sp two-line titles/10sp metadata retain system font scaling and ellipsize long text.
- Span lookup updates when switching between pages, and poster styling rebinds even when the same anime appears on both pages.

## Player

- Shared `PlayerGlass` surfaces use dark translucent gradients, thin lavender borders, rounded corners, ripple feedback and consistent 24dp vector icons. Back, current-server and overflow targets remain 48dp tall.
- Server dialog uses a scrollable bounded list, purple selected state and checkmark. Selecting the active server avoids a redundant reload. Selecting another server retains the existing playback-position and play/pause handoff.
- Overflow opens a compact glass episode menu showing neighboring episode numbers and names, disabled boundary actions and the existing orientation/fullscreen action.
- Dialogs use 180ms opening and 130ms closing window animations. Android animation settings apply normally. Dialogs are dismissed when the player stops.
- Android 12+ requests compositor background/window blur. Android may disable cross-window blur (device support, power saving or system settings). Android 8–11 retains the translucent gradient fallback. No per-frame video readbacks or blur of the controls/video tree is introduced. Inline controls use translucent glass surfaces; native video remains sharp.
- APIs, resolver, Media3 buffering/decoding, quality controls, watch history and storage paths remain unchanged.

## Modified source files

- `src/Yoru.Core/MobileLayout.cs`, `EpisodeNavigation.cs`: page-specific columns and safe episode boundary calculation.
- `src/Yoru.Core/Localization/strings.json`: localized episode-number label.
- `src/Yoru.Android/MainActivity.cs`, `FeedAdapter.cs`: Explore spans, compact spacing/type and recycled-card styling.
- `src/Yoru.Android/PlayerActivity.cs`, `PlayerGlass.cs`, `StreamIcons.cs`: glass player controls, server/episode windows and vector icons.
- `src/Yoru.Android/Resources/anim/player_glass_enter.xml`, `player_glass_exit.xml`, `Resources/values/player_styles.xml`: dialog transitions.
- `src/Yoru.Android/Yoru.Android.csproj`: update versionCode to 90312, same application ID and signing identity.
- `tests/Yoru.Core.Checks/Program.cs`: Explore-only geometry and episode-boundary regressions.

## Verification

Release APK build, portable regression checks, APK signature/metadata verification and Windows-source preservation checks are recorded under `evidence/player-glass-*`.

No Android device is connected. Native rendering, blur support, touch interactions, scrolling FPS and actual playback must still be checked on a phone. Portable tests are not a device UI/playback test.

Suggested phone checks: Explore at portrait/landscape and English/Arabic; scroll deep, open anime and Back; open another tab and return; play an episode, reveal controls, select current/different servers; cancel server picker; test Previous/Next at first/last episodes and verify resumed progress. Check Android 11 fallback and Android 12+ blur where available.
