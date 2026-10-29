# YORU Android V9.3 — glass navigation and Home tap (90304)

Reference reviewed: the provided navigation bar mockup image. The pill is dark translucent glass with posters visible beneath it, muted icons/labels and a purple Home selection.

## Navigation surface

The former combination of a 78%-opaque base, a 60%-opaque captured frost layer and a 40%-opaque tint made the pill nearly opaque. These are now approximately 19%, 22% and 22%, respectively. The surrounding bottom scrim is lighter too. Live page content remains visible through the layers; the retained frosted surface adds a subtle soft texture. Android 12+ blur is reduced from 14dp to 6dp; older Android uses radius 1 on the existing one-fifth-size capture.

The pill radius, outline, elevation, 25dp icons, label spacing, colors and 225ms purple selection indicator are unchanged. The backdrop remains initialized once per window, with no scroll/idle hook and no motion-dependent opacity, blur, tint or elevation. No recurring blur captures or work are added to scrolling. The retained low-opacity frost is a stationary surface, not continuously sampled live blur.

## Home button

- When already on Home, its navigation button starts a native RecyclerView `LinearSmoothScroller` targeting item 0 with `SnapToStart`, showing the hero's full top edge, logo and Search.
- Timing is density-independent with a short, bounded deceleration. Repeat taps do not restart the animation. A drag interrupts it; navigating elsewhere cancels it. With system animations disabled the position changes immediately.
- From another tab or details page, Home opens at position 0 using the existing Home data. Initial top positioning remains guarded during skeleton/data commits. Ordinary Back navigation retains its previous session-position behavior.
- Reselecting Home does not resubmit the feed, bootstrap Home, clear caches, or request catalog/API data. Other tab reselection is also a no-op.

## Files

`src/Yoru.Android/FloatingNavigation.cs`, `MainActivity.cs`, new `HomeSmoothScroller.cs`, and the version code in `Yoru.Android.csproj`. Supporting documentation/evidence is updated. API/resolver/player, episode fixes, history/settings storage, translations, poster dimensions and Windows source are unchanged.

## Validation

Release APK build, signature/package/ZIP checks and 12 existing core regression groups are recorded in `evidence/glass-home-*`. These cover storage, language, API fixtures, episode revisits, layout contracts and the blur algorithm; they do not test native scrolling on a device.

No Android device is connected and the emulator remains unavailable. Reference matching, actual scroll smoothness, touch behavior, RTL/insets and playback require hardware verification. Install build 90304 over the existing package without uninstalling to preserve data.

Device checklist: compare the pill over posters at rest, while dragging, during a fast fling and after stopping; it must not flash or restyle. Scroll Home down, tap Home and check a smooth exact top alignment; tap repeatedly, interrupt by dragging and navigate away mid-scroll. Enter Home from every section and check it starts at the hero top. Confirm no refresh/loading flash or new network request on Home reselection. Repeat on Android 11/12+, Arabic, landscape, gesture and three-button navigation.
