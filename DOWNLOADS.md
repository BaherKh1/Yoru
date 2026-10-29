# Episode downloads — Android build 90313

Version name 1.0, application ID `app.yoru.android`. Install the update over the existing APK; no uninstall or user-data migration is needed. Windows is unchanged.

## Using downloads

1. Open an anime's details and tap the download icon beside an episode.
2. Pick a server in the glass popup. The list comes from the same `WitcherApi.Servers` used by streaming, and media resolution uses the existing `PlaybackResolver`.
3. Pick a real quality and tap **Download**. HLS resolutions/bitrates come from the manifest. Direct files expose the server's advertised quality; an unspecified resolution is honestly labeled **Original source quality**, with no invented 360p/720p list.
4. The episode row and Android notification show progress. Open **More → Downloads** to manage jobs, retry, cancel, play completed episodes offline, or confirm deletion.

The popup supports English/Arabic and RTL, selected purple states, loading/error states, compact responsive scrolling and the shared player glass animations. Android 12+ requests compositor blur; Android 8–11 and devices that disable blur use the dark translucent fallback.

## Transfer and storage behavior

- One sequential transfer at a time keeps RAM/network use bounded and remains independent of Media3 streaming. No video transcoding or resolution reduction is performed.
- `DownloadService` is a non-exported Android `dataSync` foreground service with a low-importance notification, cancel action and a scoped partial wake lock. Notification permission is requested once on Android 13+; denial does not block user-started foreground transfers.
- Background downloads continue when navigating away or switching apps. Android/system force-stop, reboot, process termination, OEM battery restrictions or the foreground-service time budget can interrupt them. The persistent library then shows **Interrupted**, with a retry action. Transfers do not silently restart after reboot/force-stop.
- Retry refreshes the selected server through the existing API/resolver, and preserves its quality selection when still available. There is no automatic substitution with another quality. If the server/quality disappeared, the popup asks the user to choose again. Retry starts the file/archive again rather than claiming partial-byte resume.
- Episode identity prevents duplicate queued, active or completed downloads. Attempt IDs and writer completion gates prevent old cancellation/progress callbacks from overwriting a newer retry or deleting its files.
- Exact direct-file sizes use server `Content-Range`/`Content-Length`. HLS size estimates use advertised bitrate × playlist duration and carry `≈`. Unknown sizes remain unknown. Direct progress uses known total bytes; otherwise it is indeterminate with a byte count. HLS progress uses downloaded media objects (and current-object bytes when known).
- Files live in private application storage: `files/episode-downloads/<hashed-anime-and-episode-id>/`. There is no shared-storage/media permission. Existing favorites, history, playback progress, poster cache and settings stay in their existing locations.
- A `.partial` directory is never offered for playback. Completion writes an archive marker and atomically renames the folder. Completed commit markers recover a transfer if the process died before the library status write. Interrupted partial data is cleaned on retry/delete.
- The index is atomically written with a backup. Progress writes/notifications are throttled; only visible details rows are refreshed. Downloads list order stays stable during progress updates.
- Storage checks reserve 32 MiB and recheck while transferring. Failed, denied, expired, incomplete, cancelled and unsupported transfers get localized statuses rather than being marked complete.
- Deleting a download removes its local files and record, with confirmation, without deleting watch history. Offline playback uses the existing Media3 pipeline and progress storage with local file URIs; it skips server discovery, resolution and API initialization. Offline previous/next navigation is limited to downloaded episodes of that anime.

## Supported sources and explicit limits

- Public HTTPS direct media files that the existing resolver can resolve, and compatible **unencrypted, completed VOD HLS** with muxed audio/video. Both MPEG-TS and fragmented MP4 (`EXT-X-MAP`) archives are supported.
- Only the selected HLS rendition is saved. Every segment/init URI is rewritten to a local file; the offline playlist contains no remote media references.
- Live HLS, encrypted/DRM HLS or session keys, DASH, external audio/video/subtitle rendition groups, byte-range HLS, I-frame-only playlists and explicit `ALLOW-CACHE:NO` sources are rejected. MP4 protection metadata (`pssh`, `encv`, `enca`) is rejected. Unsupported formats remain available to the unchanged streaming player when it can play them.
- Server HTTP denial/rate limits and HTTPS policy are respected. The feature obtains no decryption keys, licenses, authentication credentials or account cookies, executes no scripts, and adds no restriction-bypass path. It cannot infer legal download permission from an ordinary successful media response; only download content you are authorized to save.
- Android controls background service availability. API 35+ data-sync services have a system time limit and the service handles `onTimeout` by cancelling and stopping. See [Android foreground-service timeouts](https://developer.android.com/develop/background-work/services/fgs/timeout). Manifest/rendition interpretation follows [RFC 8216](https://www.rfc-editor.org/rfc/rfc8216.html).

## Verified checks

`evidence/downloads-tests.txt` and `core-checks.json`: 17 portable regression groups pass, including the fixture-backed end-to-end path:

**Episode + chosen server → actual quality inspection → persistent queue → direct/HLS transfer → atomic local archive → restart recovery → network-free local reads → deletion.**

New cases cover direct 360p/720p source distinction, actual file size, no fabricated unknown-resolution options, HLS master variants, selected-rendition bytes, MPEG-TS and fMP4 initialization localization, progress, duplicate races, late-attempt isolation, cancellation, retry, persistent removal, disk-space failure, truncated transfer, HTTP denial/expiry, DRM/live/byte-range/external-track rejection, translations and Downloads → Back → More.

`evidence/downloads-host-decode.json` records host FFmpeg decoding of the saved H.264/AAC 720p direct file, MPEG-TS HLS archive and fragmented-MP4 HLS archive with network protocols disabled. Synthetic fixtures belong to this project; no third-party episode was downloaded for testing. FFmpeg is a host-only test tool under excluded `toolchain/test-media`, not an app dependency. Recreate fixtures with `python scripts/make_download_fixtures.py` after provisioning that test tool; all generated fixtures are included in the source package.

Release compilation, signature, version/permissions/service metadata, original featured assets and Windows preservation are recorded in `evidence/downloads-*`.

**No Android device is connected.** Native popup appearance, notification permission prompts, OEM background behavior and actual Media3 offline playback on the phone still require device verification. Host decoding/portable tests do not replace that check.

## Phone checks

1. Install `YORU-Android-V1.0-downloads.apk` over the installed YORU app. Confirm existing favorites/history/settings remain.
2. Details → download icon → switch servers → select an advertised quality → Download. Verify progress on the episode and under More → Downloads.
3. Navigate away, press Android Home and switch off the screen briefly; check the notification/progress. Test its cancel action and retry from Downloads.
4. Complete a direct source and a compatible HLS source. Enable airplane mode, reopen Downloads and play both; seek, pause/resume and check audio/video. Relaunch and verify saved progress and statuses.
5. Test repeated taps/duplicate requests; cancel then immediately retry; delete a completed entry after confirmation. Confirm history remains and deleted files stay gone after relaunch.
6. Test Arabic RTL, portrait/landscape, unavailable server/quality, expired links and insufficient storage. Confirm More stays selected and Back returns to More.
7. On Android 15+, the system timeout can be shortened for testing with the official commands in the linked Android guide. Verify interruption is shown and the app stays responsive.
