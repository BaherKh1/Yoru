using Yoru;
using Yoru.Core;

internal static class OfflineChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static void Run()
    {
        // Online-only sections are blocked; local sections stay usable.
        foreach (var page in new[] { "Home", "Explore", "Trending", "Seasonal", "Genres", "Details" })
            Check(OfflineNavigation.OnlineOnly(page), "Online-only page was treated as local: " + page);
        foreach (var page in new[] { "Downloads", "More", "Settings", "MyList", "History" })
            Check(!OfflineNavigation.OnlineOnly(page), "Local page was treated as online-only: " + page);

        // Launch offline always lands on downloads, even from a saved Home/Details state.
        Check(OfflineNavigation.StartPage(false, "Home") == "Downloads", "Offline launch did not open downloads");
        Check(OfflineNavigation.StartPage(false, "Details") == "Downloads", "Offline launch kept a details page");
        Check(OfflineNavigation.StartPage(false, "Downloads") == "Downloads", "Offline launch moved away from downloads");
        Check(OfflineNavigation.StartPage(false, "History") == "History", "Offline launch lost a local page");
        Check(OfflineNavigation.StartPage(true, "Home") == "Home", "Online launch was redirected");
        Check(OfflineNavigation.StartPage(true, "Details") == "Details", "Online launch lost details");

        // Reconnect only hands back a page that still makes sense.
        Check(OfflineNavigation.RestorePage(true, "Home") == "Home" && OfflineNavigation.RestorePage(true, "Details") == "Details", "Reconnect did not restore an online page");
        Check(OfflineNavigation.RestorePage(false, "Home") is null, "Restore page accepted an offline state");
        Check(OfflineNavigation.RestorePage(true, "Downloads") is null && OfflineNavigation.RestorePage(true, "History") is null, "Restore page claimed a local page");

        Check(OfflineNavigation.TabEnabled(false, "Home") == false && OfflineNavigation.TabEnabled(false, "Explore") == false, "Online tab stayed enabled offline");
        Check(OfflineNavigation.TabEnabled(false, "History") && OfflineNavigation.TabEnabled(false, "More"), "Local tab was disabled offline");
        Check(OfflineNavigation.TabEnabled(true, "Home") && OfflineNavigation.TabEnabled(true, "Explore"), "Online tab stayed disabled online");

        // Debounce: a blip and a network hand-over keep the user online, a sustained
        // loss switches once, and a confirmation is applied immediately.
        var state = new ConnectivityState(true);
        Check(state.Update(false, 0) == ConnectivityChange.None && state.Online, "A single missed probe dropped the connection");
        Check(state.Pending, "A pending loss was not tracked");
        Check(state.Update(false, ConnectivityState.OfflineGraceMilliseconds - 1) == ConnectivityChange.None && state.Online, "Offline grace ended too early");
        Check(state.Update(false, ConnectivityState.OfflineGraceMilliseconds) == ConnectivityChange.WentOffline && !state.Online && !state.Pending, "Sustained loss did not enter offline mode");
        Check(state.Update(false, 5000) == ConnectivityChange.None, "Offline mode was announced repeatedly");
        Check(state.Update(true, 5001) == ConnectivityChange.CameOnline && state.Online, "Reconnection was not applied immediately");
        Check(state.Update(false, 5002) == ConnectivityChange.None, "A rejected reconnection left pending state");
        Check(state.Update(true, 5002) == ConnectivityChange.None && state.Online && !state.Pending, "A queued confirmation was still pending");
        Check(state.Update(true, 9000) == ConnectivityChange.None, "Reconnection was announced repeatedly");

        // Deep offline (no network at all) commits without waiting out the grace.
        var hard = new ConnectivityState(true);
        Check(hard.Update(false, 0, true) == ConnectivityChange.WentOffline && !hard.Online && !hard.Pending, "A platform report that no network exists still waited");

        // Wi-Fi <-> mobile data hand-over inside the grace window: no flip at all.
        var handover = new ConnectivityState(true);
        Check(handover.Update(false, 0) == ConnectivityChange.None && handover.Online, "Network switch dropped the connection");
        Check(handover.Update(true, 350) == ConnectivityChange.None && handover.Online, "Network switch restored as a transition");

        // A longer real disconnect heals itself on its own without a second event.
        var flapping = new ConnectivityState(true);
        Check(flapping.Update(false, 0) == ConnectivityChange.None, "Blip started an offline transition");
        Check(flapping.Update(true, 300) == ConnectivityChange.None && flapping.Online, "Blip recovery reported a change");
        Check(flapping.Update(false, 400) == ConnectivityChange.None, "Blip recovery did not reset the grace window");
        Check(flapping.Update(false, 1000) == ConnectivityChange.WentOffline, "Sustained loss after a blip did not commit");
        Check(flapping.Update(true, 1010) == ConnectivityChange.CameOnline, "Recovery after a blip was not immediate");

        // Offline launch: the initial state is offline and never flips by itself.
        var launchedOffline = new ConnectivityState(false);
        Check(launchedOffline.Update(false, 0) == ConnectivityChange.None && !launchedOffline.Online, "Offline launch reported a transition");
        Check(launchedOffline.Update(true, 10) == ConnectivityChange.CameOnline, "Offline launch could not recover");

        Check(L.Entries["OnlineBack"][0] == "You're back online!", "Reconnect message differs from requested copy");
        foreach (var key in new[] { "OfflineNotice", "OnlineBack", "OfflineTabBlocked", "OfflineMode", "OfflineDownloadsHint", "OfflineNotDownloaded", "OfflinePlaybackUnavailable", "OfflineDownloadUnavailable" })
            Check(L.Entries[key][1].Any(c => c >= '\u0600' && c <= '\u06ff'), "Offline Arabic translation is invalid: " + key);
    }
}
