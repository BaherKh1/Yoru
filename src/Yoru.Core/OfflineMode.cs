namespace Yoru.Core;

public enum ConnectivityChange { None, WentOffline, CameOnline }

// Debounced view of real internet reachability. A genuine loss reaches the UI
// immediately when the platform says no network can answer at all, while a
// hand-over between Wi-Fi and mobile data is absorbed so it never produces a
// false offline -> online flip.
public sealed class ConnectivityState
{
    // Absorbs Wi-Fi <-> data hand-over gaps; also short enough that a real
    // disconnect is still noticed in well under a second.
    public static readonly long OfflineGraceMilliseconds = 600;

    public ConnectivityState(bool online) => Online = online;
    public bool Online { get; private set; }
    public bool Pending => pending is not null;
    private bool? pending;
    private long pendingSince;

    // `definitive` marks a platform answer that no hand-over can follow.
    public ConnectivityChange Update(bool reachable, long nowMilliseconds, bool definitive = false)
    {
        if (reachable == Online) { pending = null; return ConnectivityChange.None; }
        if (pending != reachable) { pending = reachable; pendingSince = nowMilliseconds; }
        // A hand-over gap absorbs an offline vote; confirmation comes back at once.
        var grace = definitive || reachable ? 0 : OfflineGraceMilliseconds;
        if (nowMilliseconds - pendingSince < grace) return ConnectivityChange.None;
        pending = null;
        Online = reachable;
        return reachable ? ConnectivityChange.CameOnline : ConnectivityChange.WentOffline;
    }
}

// Pages that cannot work without the network. Downloads, More, Settings, MyList
// and History stay available because they read local storage only.
public static class OfflineNavigation
{
    public static bool OnlineOnly(string page) => MobileNavigation.NormalizePage(page) is "Home" or "Explore" or "Seasonal" or "Genres" or "Details";
    public static bool TabEnabled(bool online, string tab) => online || !OnlineOnly(tab);
    public static string StartPage(bool online, string page)
    {
        var normalized = MobileNavigation.NormalizePage(page);
        return online || !OnlineOnly(normalized) ? normalized : "Downloads";
    }
    public static string? RestorePage(bool online, string? page)
    {
        if (page is null) return null;
        var normalized = MobileNavigation.NormalizePage(page);
        return online && OnlineOnly(normalized) ? normalized : null;
    }
}
