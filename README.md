# YORU

YORU ships in two flavors in this repository:

- **Android app** (`src/`): native .NET for Android (net10.0-android) client using AndroidX RecyclerView and **Media3/ExoPlayer** — self-contained, with its own storage, cache and preferences.
- **Windows desktop client** (`windows/`): the original .NET 8 WPF application (`Yoru`) with the LibVLCSharp playback engine, sharing the same core network/resolver/localization sources with the Android client.

Both target Android 8.0 (API 26) / Windows and share the same media pipeline approach, server handling, download model, and EN/AR localization feature set.

Android package ID `app.yoru.android` · Android 8.0 (API 26)+ · arm64-v8a / x86-64.

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

### Android app

```powershell
# One-time environment setup (downloads into ./toolchain, fully offline afterwards)
./setup.ps1

# Restore + build + sign a Release APK into artifacts/YORU-Android-V1.0.apk
./build.ps1 -JavaSdkDirectory "<path to JDK 21>"

# Run the core regression suite (20 offline-testable groups)
./test.ps1
```

If your environment already provides `dotnet`, a JDK and an Android SDK, `build.ps1` accepts `-AndroidSdkDirectory` and falls back to `dotnet` on `PATH`; you can skip `setup.ps1` entirely. See [BUILD.md](BUILD.md) for every option, including keystore-based release signing.

### Windows desktop client (WPF)

`windows/` contains the .NET 8 WPF version (`Yoru`, `net8.0-windows`) — the shared core (`CatalogApi`, `PlaybackResolver`, caches, localization) used by both clients. Build it with the .NET SDK:

```powershell
dotnet restore windows/YORU.Desktop.csproj --configfile windows/NuGet.Config
dotnet publish windows/YORU.Desktop.csproj -c Release -o windows/dist
```

The publish step bundles the native VLC playback engine from an installed VLC 3 x64 (`libvlc.dll`, `plugins`, …), or see `windows/build.ps1` which performs the full icon/publish/bundle sequence. Application data lives in `%LOCALAPPDATA%/YORU`; `windows/certs/` holds the public Mozilla CA bundle used for HTTPS validation (no private keys).

Build outputs (`toolchain/`, `bin/`, `obj/`, `artifacts/`, `release/`) are git-ignored and never required in the repository.

**Signing:** CI/start builds use the standard Android development certificate, which is fine for sideloading and testing. For production, supply your own keystore via the documented MSBuild properties. Never commit keystores or their passwords.

## Release

The delivered APK is written to `artifacts/` by `build.ps1`. Release-ready payloads for GitHub Releases live in `release/`, which is also git-ignored so binaries stay out of repository history. Attach them as GitHub Release assets instead (`YORU-Android-V1.0.apk` and `YORU-Windows-V1.0.zip` — the Windows zip is the cleaned `windows/dist` package without debug symbols or build-diagnostics JSON).

## Repository layout

```
├── src/
│   ├── Yoru.Android/      # Native Android app (activities, views, services, connectivity)
│   └── Yoru.Core/         # Shared core: API, playback resolution, downloads, offline contracts
├── windows/               # Windows WPF desktop client (Yoru, .NET 8)
│   ├── Assets/            # Icons, branding artwork and icon build scripts
│   ├── Controls/ Themes/  # WPF controls and styling
│   ├── Localization/      # Embedded EN/AR strings
│   └── tests/             # Windows-side API/decoder/performance checks
├── tests/
│   └── Yoru.Core.Checks/  # Portable Android regression suite (run by ./test.ps1)
├── scripts/               # Checksum-verified SDK/workload/package bootstrap helpers
├── build.ps1              # Android Release APK build
├── setup.ps1              # One-time Android toolchain bootstrap
└── test.ps1               # Android core test runner
```

Design and validation history are documented in [ARCHITECTURE.md](ARCHITECTURE.md), [DEVICE-TESTS.md](DEVICE-TESTS.md), [VALIDATION.md](VALIDATION.md) and the dated update notes (`*-UPDATE.md`, `*-FIX.md`) at the repository root.

## Testing status

The portable core suite (catalog, resolver, server validation, downloads, offline navigation and connectivity contracts) passes fully offline. Install/device checks follow [DEVICE-TESTS.md](DEVICE-TESTS.md); on-device UI, hardware decoding, battery and A/V synchronization results depend on the physical device and are reported in [VALIDATION.md](VALIDATION.md).

## Third-party components

Media3/ExoPlayer, AndroidX, Microsoft .NET for Android and other dependencies are listed with their licenses in [THIRD-PARTY.md](THIRD-PARTY.md).

## Notes for contributors

- Never commit keystores (`*.jks`, `*.keystore`), passwords, tokens or generated build output — these patterns are already covered by [.gitignore](.gitignore).
- Use per-project neutral paths in documentation only; no absolute machine paths should appear in tracked files.
