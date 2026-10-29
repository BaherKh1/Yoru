# YORU for Android

A native anime streaming and offline playback app for Android, built with **.NET for Android (net10.0-android)**, AndroidX RecyclerView, and **Media3/ExoPlayer**. It is a self-contained Android project — not a desktop wrapper — with its own storage, cache and preferences.

Package ID `app.yoru.android` · Android 8.0 (API 26)+ · arm64-v8a / x86-64.

## Features

- **Home** — live catalog with cached startup fallback, featured artwork and Continue Watching.
- **Explore** — infinitely paged, cancellable search with responsive poster grids, sorting and saved scroll positions.
- **Player** — full-screen Media3 player: seeking, auto-hiding controls, server/quality selection, previous/next episode, progress resume, hardware decoding and audio focus. Source MIME types (progressive, HLS, fMP4-HLS) are preserved during selection.
- **Downloads** — real server/quality selection, background transfers with progress/cancel, persistent job queue inside private app storage, and offline playback of completed episodes.
- **Offline mode** — without a connection the app opens Downloads, dims network-only sections, plays downloaded episodes, detects reconnect automatically ("You're back online!") and re-enables everything without a restart. Detection uses actual reachability (validated platform state plus a probe fallback), not merely whether Wi-Fi/mobile data is enabled.
- **i18n** — English default with a locally saved Arabic option, RTL layout, instant language switch.

## Requirements

- **JDK 21** (any installed location)
- **.NET SDK 10.0.403** and the **.NET Android workload** — bootstrap automatically via `setup.ps1` (needs Python 3 with no extra packages)
- The Android SDK is fetched automatically (API 36, build-tools 36.0.0) during setup

## Build

```powershell
# One-time environment setup (downloads into ./toolchain, fully offline afterwards)
./setup.ps1

# Restore + build + sign a Release APK into artifacts/YORU-Android-V9.3.apk
./build.ps1 -JavaSdkDirectory "<path to JDK 21>"

# Run the core regression suite (20 offline-testable groups)
./test.ps1
```

If your environment already provides `dotnet`, a JDK and an Android SDK, `build.ps1` accepts `-AndroidSdkDirectory` and falls back to `dotnet` on `PATH`; you can skip `setup.ps1` entirely. See [BUILD.md](BUILD.md) for every option, including keystore-based release signing.

Build outputs (`toolchain/`, `bin/`, `obj/`, `artifacts/`, `release/`) are git-ignored and never required in the repository.

**Signing:** CI/start builds use the standard Android development certificate, which is fine for sideloading and testing. For production, supply your own keystore via the documented MSBuild properties. Never commit keystores or their passwords.

## Release

The delivered APK is written to `artifacts/` by `build.ps1`. Release-ready APKs for GitHub Releases live in `release/`, which is also git-ignored so APK binaries stay out of repository history. Attach them as GitHub Release assets instead.

## Repository layout

```
├── src/
│   ├── Yoru.Android/      # Native Android app (activities, views, services, connectivity)
│   └── Yoru.Core/         # Shared core: API, playback resolution, downloads, offline contracts
├── tests/
│   └── Yoru.Core.Checks/  # Portable regression suite (run by ./test.ps1)
├── scripts/               # Checksum-verified SDK/workload/package bootstrap helpers
├── build.ps1              # Release APK build
├── setup.ps1              # One-time toolchain bootstrap
└── test.ps1               # Core test runner
```

Design and validation history are documented in [ARCHITECTURE.md](ARCHITECTURE.md), [DEVICE-TESTS.md](DEVICE-TESTS.md), [VALIDATION.md](VALIDATION.md) and the dated update notes (`*-UPDATE.md`, `*-FIX.md`) at the repository root.

## Testing status

The portable core suite (catalog, resolver, server validation, downloads, offline navigation and connectivity contracts) passes fully offline. Install/device checks follow [DEVICE-TESTS.md](DEVICE-TESTS.md); on-device UI, hardware decoding, battery and A/V synchronization results depend on the physical device and are reported in [VALIDATION.md](VALIDATION.md).

## Third-party components

Media3/ExoPlayer, AndroidX, Microsoft .NET for Android and other dependencies are listed with their licenses in [THIRD-PARTY.md](THIRD-PARTY.md).

## Notes for contributors

- Never commit keystores (`*.jks`, `*.keystore`), passwords, tokens or generated build output — these patterns are already covered by [.gitignore](.gitignore).
- Use per-project neutral paths in documentation only; no absolute machine paths should appear in tracked files.
