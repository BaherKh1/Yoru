# Android install and device test checklist

These are pending device checks, not a record that they already passed.

## Install

Use an Android 8.0+ ARM64 phone/tablet or x86-64 Android emulator. Copy the APK to the device and allow that trusted installer to install this APK, or enable USB debugging and run:

```powershell
./toolchain/android-sdk/platform-tools/adb.exe devices
./toolchain/android-sdk/platform-tools/adb.exe install -r artifacts/YORU-Android-V1.0.apk
./toolchain/android-sdk/platform-tools/adb.exe shell monkey -p app.yoru.android -c android.intent.category.LAUNCHER 1
```

No physical device was connected during development. The local Android 15 emulator image was installed, but the hypervisor driver is absent and forced software boot failed. For emulator testing, enable virtualization and install/configure a supported accelerator through Android Studio on a compatible host; this may require administrator access/reboot. No driver or BIOS changes were made here. An accelerator is unnecessary to build or sideload the APK.

## Navigation and data

For build 90302 also verify the cinematic redesign:

1. Install as an update with `adb install -r`; confirm existing favorites, language and history survive. Check the retained startup workaround on the Redmi Note 8 Pro / Android 11.
2. On Home, check artwork under the white status icons, safe logo/search placement, readable gradient/title/genres, Watch Now and My List. Swipe the hero horizontally and vertically; horizontal gestures must change real featured titles without accidentally opening details, while vertical gestures scroll the feed. Search must open Explore and the keyboard.
3. Rapidly tap all five bottom tabs and enter/exit More subpages. The indicator must slide from its current position with no resets or duplicate indicators. Repeat in Arabic and with animation duration disabled in system accessibility settings.
4. Scroll the last catalog/history/episode row above the floating bar. Test gesture and three-button navigation, display cutouts, keyboard open/close, portrait/landscape, 320–1280dp widths and 100–200% font sizes. Confirm no two scrollbars or covered action buttons.
5. On Android 11 and 12+, scroll/fling and stop over posters. Frost should update after stopping without bitmap captures during the fling. Use Android Studio frame/memory profiling for actual device measurements; desktop blur timing is not a scrolling FPS measurement.
6. Verify More row icons, grouping/dividers, chevrons, Settings language changes, episode search and unchanged playback/server/quality controls.

1. Fresh install: English, YORU logo and dark/purple theme; Home fetches real titles. Relaunch with cache: saved Home data appears while refresh runs. Offline cold/warm launch: meaningful retry/cache state.
2. Open every bottom tab; use More for My List, Genres, Seasonal and Settings. Verify no system status/navigation-bar overlap, and back returns to the previous details/catalog screen.
3. Explore: search English/Arabic, scroll through multiple batches, ensure no duplicates, sort by rating, open a title and return with scroll position retained. Verify genre selection and seasonal year metadata; rankings use fetched pages.
4. Save/remove favorites, restart the app and confirm persistence. Resume an episode, return to Home/History and confirm progress. Remove one history entry, then clear all after confirmation; confirm favorites remain and deleted progress stays deleted after restart.
5. Switch language to Arabic in Settings. Verify immediate RTL, native dialogs, original anime metadata and player controls. Switch back and restart to verify preference persistence.

## Player

1. Choose an episode (for example Jujutsu Kaisen episode 1), verify streaming stays in the native player. If a host fails, select another server/quality. Server availability and geography can vary.
2. Verify touch controls, scrub seeking, pause/resume, server/quality switching at the same timestamp, episode previous/next, player menu rotation and fullscreen. Check portrait/landscape and gesture/three-button navigation bars.
3. Watch at least five minutes; check buffering, dropped frames, quality and actual speaker/display sync. Seek forward/back repeatedly; switch servers and episodes; background/foreground the app; unplug headphones and test audio focus. Verify background playback stops and progress is saved.
4. Finish an episode, replay it and verify progress resumes tracking. Check expired/unavailable media gracefully offers another server.

## Screens and performance

Test 320-412dp phones and 600-1280dp tablets, portrait/landscape, system font scaling, RTL, display cutouts, and Android 8/11/15/16 where available. Scroll hundreds/thousands of catalog entries and check recycled cards do not show another anime's image. Monitor memory/network/battery with Android Studio; the existing geometry/cache tests do not measure real FPS or A/V sync.

For a launch failure capture logs:

```powershell
./toolchain/android-sdk/platform-tools/adb.exe logcat -c
# Launch YORU and reproduce, then:
./toolchain/android-sdk/platform-tools/adb.exe logcat -d -s AndroidRuntime mono-rt DOTNET > yoru-device-log.txt
```

Review logs before sharing; do not include temporary stream URLs or client configuration credentials.

## Build 90303 corrections

Use the device regression checklist in NAVIGATION-DETAILS-FIXES.md. In particular, the navigation frost must now remain identical during dragging/flinging/idle, rather than updating when scrolling stops. Verify Home starts at image offset 0 and repeated A/back/B/back/A visits never show wrong artwork or lose episodes.

## Build 90304

Use the reference/style and Home-tap checks in GLASS-HOME-UPDATE.md. Test repeat Home taps, user interruption, changing tabs during scrolling, exact hero-top alignment and absence of API requests on reselection.

## Build 90305

Use the reference/Explore checklist in REFERENCE-EXPLORE-UPDATE.md. Verify active Explore taps, entry from other sections, preserved queries/results, interrupted/repeated animations and stable glass styling.


## Build 90313 downloads

Run the phone checklist in [DOWNLOADS.md](DOWNLOADS.md): server/quality selection, progress, notifications, cancellation/retry, restart persistence, airplane-mode playback, deletion, EN/AR RTL and More navigation. Host fixtures and decoding pass, but no Android device is connected; device validation remains pending.


## Build 90314

Follow the checklist in RESPONSIVE-SERVERS-UPDATE.md for responsive Explore resizing, EN/AR left-positioned download targets, both quality-grouped pickers, unavailable/default fallback, all-unavailable Retry/Cancel, player switching/episode navigation/background races and grouped live-progress cards. No adb device is connected; these native checks remain pending.
