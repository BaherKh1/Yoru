using Android.Content;
using Android.Net;
using Android.OS;
using Yoru.Core;
using OperationCanceledException = System.OperationCanceledException;

namespace Yoru.AndroidApp;

// Real internet access, not "Wi-Fi or mobile data is switched on".
//
// The platform network callback is the primary source: OnLost/OnAvailable/
// OnCapabilitiesChanged cover every network change, including Wi-Fi <-> mobile
// data switches, and each event is verified against the live default network.
// A short probe confirms networks the platform cannot validate itself, and a
// slow poll keeps detection alive if a callback is ever missed. The debounced
// ConnectivityState absorbs switching gaps so a hand-over does not produce a
// spurious offline -> online flip, while a genuine loss still reaches the UI
// in well under a second.
internal sealed class ConnectivityMonitor : ConnectivityManager.NetworkCallback
{
    // Follow-ups run right after a network callback so a switch resolves itself
    // quickly; the grace window stays inside that delay.
    private const long RecheckDelayMs = 350;
    private const long OnlineIntervalMs = 3000;
    private const long OfflineIntervalMs = 1200;
    private const long PendingIntervalMs = 180;
    private const long ProbeCooldownReachedMs = 8000;
    private const long ProbeCooldownFailedMs = 2500;

    private static readonly string[] Endpoints =
    {
        "https://firestore.googleapis.com/",
        "https://www.gstatic.com/generate_204"
    };
    private static readonly HttpClient ProbeClient = CreateProbeClient();
    private static HttpClient CreateProbeClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }) { Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YORU/0.1");
        return client;
    }

    private readonly ConnectivityManager manager;
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly ConnectivityState state;
    private readonly Java.Lang.Runnable poll;
    private readonly Java.Lang.Runnable recheck;
    private bool started, probing, disposed;
    private long lastProbeAt;

    public event Action<bool>? Changed;

    // App-wide truth, shared by every activity and dialog. The debounced decision
    // is the only offline verdict; dialogs cannot disagree with the navigation.
    public static bool Online { get; private set; } = true;
    public static bool Decided { get; private set; }
    public static bool Offline => Decided && !Online;
    public static bool? LastProbe { get; private set; }

    public ConnectivityMonitor(Context context)
    {
        manager = (ConnectivityManager)context.GetSystemService(Context.ConnectivityService)!;
        // The last decision stands until re-evaluated; a fresh process with no
        // network at all starts offline, and a network confirmed recently keeps
        // working even when the platform cannot validate it.
        state = new ConnectivityState(ReachableSeed());
        poll = new Java.Lang.Runnable(Poll);
        recheck = new Java.Lang.Runnable(Recheck);
    }

    public bool Current => state.Online;

    private static bool ReachableSeed()
    {
        if (Offline) return false;
        var capabilities = PlatformCapabilities();
        if (capabilities is not { Internet: true }) return false;
        return capabilities.Value.Validated || LastProbe is not false;
    }

    private static (bool Internet, bool Validated)? PlatformCapabilities()
    {
        try
        {
            return Application.Context.GetSystemService(Context.ConnectivityService) is not ConnectivityManager manager
                ? null : NetworkCapabilities(manager);
        }
        catch (Exception) { return null; }
    }

    private static (bool Internet, bool Validated)? NetworkCapabilities(ConnectivityManager manager)
    {
        var network = manager.ActiveNetwork;
        if (network is null) return null;
        var capabilities = manager.GetNetworkCapabilities(network);
        if (capabilities is null) return null;
        return (capabilities.HasCapability(NetCapability.Internet), capabilities.HasCapability(NetCapability.Validated));
    }

    // One-shot platform check for code that runs before any monitor exists.
    public static bool Reachable(Context context)
    {
        try
        {
            return context.GetSystemService(Context.ConnectivityService) is not ConnectivityManager manager
                ? false : NetworkCapabilities(manager) is { Internet: true, Validated: true };
        }
        catch (Exception) { return false; }
    }

    public void Start()
    {
        if (started || disposed) return;
        started = true;
        try { manager.RegisterDefaultNetworkCallback(this); }
        catch (Exception error) { Console.WriteLine("[YORU] Connectivity callback unavailable: " + error.GetType().Name); }
        handler.Post(recheck);
    }

    public void Stop()
    {
        if (!started) return;
        started = false;
        handler.RemoveCallbacksAndMessages(null);
        try { manager.UnregisterNetworkCallback(this); } catch (Exception) { }
    }

    public override void OnAvailable(Network network) => Invalidate();
    public override void OnLost(Network network) => Invalidate();
    public override void OnCapabilitiesChanged(Network network, NetworkCapabilities capabilities) => Invalidate();
    public override void OnUnavailable() => Invalidate();

    // Network events replace whatever re-check was queued and ask for a fresh
    // evaluation on the main thread.
    private void Invalidate()
    {
        handler.RemoveCallbacks(recheck);
        handler.PostDelayed(recheck, 0);
    }

    private void Recheck()
    {
        if (disposed) return;
        Evaluate();
    }

    private void Poll()
    {
        if (!started || disposed) return;
        Evaluate();
    }

    private void Evaluate()
    {
        var capabilities = NetworkCapabilities(manager);
        if (capabilities is not { } value)
        {
            // Nothing is usable at the moment; a short grace absorbs a hand-over
            // between wireless links while a validated loss is already evented.
            Apply(false, false);
            return;
        }
        if (!value.Internet) { Apply(false, false); return; }
        if (value.Validated) { Apply(true, false); return; }
        // Internet-capable but unconfirmed: ask the network itself, and keep
        // applying the previous verdict so the debounce can commit cleanly.
        if (!probing) _ = Probe();
        if (LastProbe is { } verdict) Apply(verdict, false);
        else Schedule(probing ? PendingIntervalMs : RecheckDelayMs);
    }

    private async Task Probe()
    {
        // Servers are probed rarely; only a failed network gets rechecked quickly
        // so recovery is still detected when the platform stays unvalidated.
        var now = SystemClock.ElapsedRealtime();
        var cooldown = LastProbe is true ? ProbeCooldownReachedMs : LastProbe is false ? ProbeCooldownFailedMs : 0;
        if (lastProbeAt != 0 && now - lastProbeAt < cooldown) return;
        lastProbeAt = now;
        probing = true;
        var reachable = false;
        try { reachable = await Reachable(); }
        catch (Exception error) { Console.WriteLine("[YORU] Connectivity probe failed: " + error.GetType().Name); }
        probing = false;
        if (disposed) return;
        LastProbe = reachable;
        handler.Post(() => { if (started) Evaluate(); });
    }

    private static async Task<bool> Reachable()
    {
        foreach (var endpoint in Endpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                // Any HTTP answer proves the network path works; the status is irrelevant.
                using var response = await ProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                return true;
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }
        return false;
    }

    private void Apply(bool reachable, bool definitive)
    {
        if (disposed) return;
        handler.Post(() =>
        {
            if (disposed) return;
            var now = SystemClock.ElapsedRealtime();
            var change = state.Update(reachable, now, definitive);
            Online = state.Online;
            Decided = true;
            ScheduleNext();
            if (change == ConnectivityChange.None) return;
            Console.WriteLine("[YORU] Connectivity " + (state.Online ? "online" : "offline"));
            // A single event per decision; the activity guards repeats itself.
            Changed?.Invoke(state.Online);
        });
    }

    // Keep the fastest useful interval for the current situation and replace
    // whatever was queued before: callbacks keep the app event-driven, the poll
    // is the fallback when no callback arrives.
    private void ScheduleNext()
    {
        Schedule(!state.Online ? OfflineIntervalMs : state.Pending ? PendingIntervalMs : OnlineIntervalMs);
    }

    private void Schedule(long delayMilliseconds)
    {
        handler.RemoveCallbacks(poll);
        if (started && !disposed) handler.PostDelayed(poll, delayMilliseconds);
    }

    public void Release()
    {
        if (disposed) return;
        Stop();
        disposed = true;
        handler.Dispose();
    }
}
