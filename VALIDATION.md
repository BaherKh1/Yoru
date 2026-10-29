# Android V1.0 validation and limits

Latest build: **90317**, with 20 portable core groups passing. New coverage validates the offline navigation contract (launch redirect, online-only/tap gating, local-page preservation) and the connectivity debounce (grace boundaries, definitive loss commit, Wi-Fi↔data hand-over absorption, immediate recovery, single-shot events, EN/AR copy). See [RESPONSIVE-SERVERS-UPDATE.md](RESPONSIVE-SERVERS-UPDATE.md) and `evidence/responsive-servers-summary.json`. Native Android UI, background behavior and device playback remain unverified.

## Verified

- Ran the delivered build.ps1 against the final source: Release SignAndroidPackage build succeeded with zero warnings/errors.
- Created a self-contained APK with embedded .NET runtime/assemblies for arm64-v8a and x86_64. Android package app.yoru.android, version 1.0 / 90314, min API 26, target/compile API 36, YORU label and adaptive icon. Native launcher activity is present.
- apksigner verify succeeded with APK v2/v3 signatures. The signer is CN=Android Debug: development/sideload certificate, not a production release key.
- APK ZIP/CRC passed. Manifest includes INTERNET, ACCESS_NETWORK_STATE and normal permissions merged by AndroidX/Media3 (WAKE_LOCK and the package-scoped dynamic-receiver permission). No storage, camera, microphone or contacts permission is requested. Media3 is released on backgrounding; the app does not implement background playback.
- Eleven portable core groups passed: reused API configuration/models/Unicode search/details/episode fallback/server mapping; extensionless HLS MIME preservation; reused MF/KF/PD/ST resolver and rejection/cancellation fixtures; persistent bounded/corrupt-safe startup cache; English defaults and EN/AR preference persistence; favorites/restart; individual/bulk history/progress deletion and stale-save protection; paged request deduplication/end/retry; shared cache request coalescing; 320-1280dp phone/tablet layout contract; live backend compatibility.
- Live host checks returned 30 catalog rows / 7065 total titles, 23 episodes and nine servers for a real Jujutsu Kaisen search candidate. MediaFire and PixelDrain resolved to validated video/mp4 resources. No API keys or resolved stream URLs were written to reports.
- This host cannot acquire .NET Schannel credentials, so the live C# API/resolver test uses the existing Node HTTPS transport bridge. That bridge is test-only and is not included in Android app code. Android uses its platform HTTP/TLS stack and Media3.
- Compared 84 Windows source/resource/config files before/after: all byte-identical. Windows code, settings, watch history and packaged executable were not changed. The Android project has an independent storage root and package identifier.

## Not verified on Android hardware/emulator

No Android device was connected. The local emulator was attempted but could not boot with the available acceleration setup; no driver or BIOS changes were made. The user explicitly selected continuation with an APK and device-test instructions.

Therefore installed Android navigation, visuals, phone/tablet screen rendering, RTL text presentation, real native playback, seeking/quality switches, hardware decoding, battery/memory/FPS and physical audio/video synchronization have NOT been device-validated. Native implementations and portable/layout contracts are present; those do not establish runtime UI or A/V results. Follow DEVICE-TESTS.md before treating this as a production-validated app.

Other limits: Android 8+ ARM64/x86-64 only, no 32-bit ABI; local data does not sync with Windows; HTTPS-only networking; server geography, availability and codec support vary. Trending/Seasonal derive their ranking/filtering from fetched real catalog pages rather than a new dedicated backend feed. The initial build disables trimming/AOT to avoid reflection/binding regressions; further size/performance optimization needs device profiling. This is a development-signed Release build.

Evidence: build-final.txt, core-checks.json, live-core-console.txt, apk-signature.txt, apk-badging.txt, windows-preservation.json, android-release-summary.json. Fixture tests are in tests/Yoru.Core.Checks. Detailed build/toolchain logs remain in the local toolchain folder and are excluded from the source archive.

## Startup compatibility update

See STARTUP-FIX.md. Release build 90301 disables JNI marshal methods, preserves the signing certificate, and explicitly registers YoruApp callbacks. The clean rebuild has zero warnings/errors; APK v2/v3 signatures and ZIP integrity pass; all 10 offline core groups pass. The 11th live group above was run on the original source. No device launch was verified for this update.

## Cinematic interface update (90302)

See CINEMATIC-REDESIGN.md and evidence/cinematic-*. Eleven offline core groups pass, including safe-area/scaled geometry and the downsampled blur algorithm. Release build succeeds without warnings/errors. Native hardware UI, touch, FPS and playback remain unverified; no Android device is connected. The earlier live-backend results above are historical, not a fresh device playback test.

## Navigation/details corrections (90303)

See NAVIGATION-DETAILS-FIXES.md. Twelve portable groups pass, including real ApiDataCache episode ownership/revisit regression coverage. Release build and signature/package checks are recorded in evidence/navigation-details-fixes-*. Native visual/touch, fresh-launch framing and playback are not device-verified. The existing frosted surface is retained without scroll/idle updates; rendering checks must be performed on hardware.

## Glass/Home update (90304)

See GLASS-HOME-UPDATE.md. Twelve core regression groups pass; native Home smooth scrolling and the updated glass appearance remain unverified on hardware. Release/package validation is recorded in evidence/glass-home-*. No backend or storage changes.

## Reference glass / Explore update (90305)

See REFERENCE-EXPLORE-UPDATE.md. Twelve portable groups pass; hardware rendering and touch scrolling remain unverified. Release/package/signature checks are in evidence/reference-explore-*. No backend or storage changes.

## More Back fix (90309)

Thirteen core groups pass, including More/subpage/details navigation-policy checks. Native hardware transitions remain unverified. See MORE-BACK-FIX.md and evidence/more-back-*. Bar styling is unchanged from the selected reference-explore baseline.

## Exact catalog return (90310)

Fourteen portable groups pass, including viewport-anchor, signed-offset and retained-session tests. Native pre-draw return behavior still requires device validation. See EXPLORE-RESTORE-FIX.md and evidence/explore-restore-*. Navbar styling, backend/player and user storage remain unchanged.

## Server availability fix (90315)

See the deliverable's release README. Discovery timeouts and transport errors are now temporary/network failures instead of "no servers"; empty results are not cached; all servers are attempted in descending quality order; resolver-verified direct media skips a redundant second probe; HTTP Range rejection falls back to a plain GET; media resolution uses the managed HTTP transport; and nested/alternate server fields are parsed. Nineteen core groups pass, including new discovery, exhaustive-attempt, Range and parsing regressions. Device playback remains unverified (no connected device; the emulator cannot boot without a hardware accelerator).

## Offline mode (90316)

See the deliverable's release README. Launch without internet opens Downloads with a one-time "You're offline" notice; Home/Explore are dimmed and tapping them (or any online-only page: Details/Seasonal/Genres/search) explains the situation instead of failing requests; Downloads/More/Settings/MyList/History keep working; downloaded episodes play offline with server switching hidden; disconnecting mid-use moves to Downloads while remembering the stranded page; reconnecting shows "You're back online!" once, re-enables everything without a restart and restores the remembered page only if the user remained on Downloads; notices use a single dark-purple pill above the floating bar and are never duplicated; new downloads are refused offline. `ConnectivityState` debounces losses (recovery commits immediately), `OfflineNavigation` is the tested page contract, and `ServerUi.Connected`/PlayerActivity read the monitor's verdict instead of raw capabilities. Eighteen-to-twenty core groups pass, including the new `OfflineChecks`; release build has zero warnings/errors.

## Connectivity detection rework (90317)

See the deliverable's release README. The existing `ConnectivityMonitor` (no second implementation) now drives detection primarily from the platform network callback — OnAvailable/OnLost/OnCapabilitiesChanged/OnUnavailable each trigger an immediate re-evaluation, so navigation updates while the user sits on any page. Actual internet access is monitored, not merely an active interface: the default network must be internet-capable and validated, and networks that block the platform check are verified with a short HTTPS probe (firestore.googleapis.com / gstatic generate_204, 3 s timeout, 8 s reached / 2.5 s failed cooldown). A fast fallback poll (3 s online / 1.2 s offline / 180 ms pending) covers missed callbacks. The debounce absorbs a Wi-Fi↔mobile-data hand-over (600 ms grace) without a false offline flip, yet commits a genuine loss in well under a second and commits immediately when no network can answer at all. The app-wide verdict is a single static (ConnectivityMonitor.Online/Decided/LastProbe) read by `ServerUi.Connected` and the player, and the notice pill suppresses an identical message within 6 s. The new `OfflineChecks` cover grace boundaries, definitive commit, hand-over absorption, immediate recovery and single-shot events; 20 groups pass and the clean release build reports zero warnings/errors. Emulator/device scenarios could not run (no device attached; no hardware accelerator available, per user choice to skip), so the on-device steps in the OfflineFix README remain to be performed.

## Home artwork / Trending removal (90311)

Fifteen portable groups pass. Original JPGs are packaged unchanged and mapped only on Home; four-tab migration/geometry is checked. Native rendering remains unverified. See FEATURED-IMAGES-UPDATE.md and evidence/featured-*.
