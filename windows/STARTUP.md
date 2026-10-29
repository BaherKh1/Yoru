# Startup investigation and verification

A live read-only service probe measured approximately 873ms for settings and 327ms for the first catalog response (1.20s total in that sample, two requests). This is a network sample through the test Node transport, not a guarantee of desktop latency. Source inspection and the supplied screenshot confirm that live Home controls previously appeared before this work completed; there was no persistent catalog snapshot.

The same hidden-WPF profiling harness runs in isolated copies of V8.1 and V9. Three fresh processes per version per cold/warm scenario use real captured catalog records/artwork and two modeled 250ms request stages. Those delays exist only in the test harness. Values below are medians in milliseconds, measured from window construction, not process creation or physical monitor latency.

| Scenario | Window constructor | First presentation | Metadata ready | First presentation |
|---|---:|---:|---:|---|
| V8.1 cold | 152.58 | 220.18 | 741.73 | Empty live hero and no Popular cards |
| V8.1 warm | 197.74 | 262.58 | 776.16 | Same empty state; disk metadata was unused |
| V9 cold | 155.19 | 227.12 | 679.19 | Matching lightweight skeletons |
| V9 warm | 245.76 | 369.48 | 245.77 | Cached title/buttons, hero image and all 8 posters |

All three V9 warm runs displayed complete cached Home data/artwork before refresh completed. All three cold runs showed placeholders rather than incomplete controls. Each run made one logical catalog fetch. Warm refresh of identical data made one total Home commit, preserving existing cards. Cold first-presentation overhead was about 7ms in this harness. Rendering real cached content in the warm first frame has more layout/image work than displaying V8.1's empty frame; the window is not held behind a network or image-ready wait.

The artwork test uses captured existing files, not a CDN warm-speed claim. Missing/expired artwork remains asynchronous and cannot block the window. Metadata cache validity and artwork cache policy are separate; metadata cache is last-known-good, while original artwork budgets/expiry remain.

Diagnostics cover cold/warm/offline/corrupt cache, EN/AR loading states, atomic snapshot writes and malformed data, changed/unchanged refreshes, duplicate startup suppression, early navigation, close cancellation and disk-only artwork priming. Existing UI/API/player checks remain in VALIDATION.md. Tests do not modify the user's history or use a real streaming source for these startup timings.

Raw reports: startup-v81/v9-cold/warm-1/2/3.json, v9-live-startup-phases.json, v9-startup-checks.json and v9-startup-summary.json in evidence. Initial-frame PNGs are alongside them. The baseline source is preserved outside this repository.
