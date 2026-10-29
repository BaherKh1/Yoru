using System.IO;
using System.Text.Json;

namespace Yoru;

// Keep legacy identifiers inside data/API models; only the storage root changes.
public static class AppStorage
{
    private static readonly Lazy<string> root = new(() => Resolve(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
    internal static string? DiagnosticRoot { get; set; }
    public static string DirectoryPath => DiagnosticRoot ?? root.Value;

    internal static string Resolve(string localRoot)
    {
        var legacy = Path.Combine(localRoot, "Yoru");
        var target = Path.Combine(localRoot, "YORU");
        try { Migrate(legacy, target); return target; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) {
            // A failed copy must not hide an existing library. Retry next launch.
            return Directory.Exists(legacy) ? legacy : target;
        }
    }

    internal static void Migrate(string legacy, string target)
    {
        using var migration = new Mutex(false, "Local\\YORUStorageMigrationV8");
        try { migration.WaitOne(); } catch (AbandonedMutexException) { }
        try {
            Directory.CreateDirectory(target);
            var marker = Path.Combine(target, "migration-v8.complete.json");
            if (File.Exists(marker)) return;
            // Persist the initial collision policy so an interrupted history /
            // tombstone pair can finish copying safely on the next launch.
            var pending=Path.Combine(target,"migration-v8.pending.json");
            var keepHistory=File.Exists(Path.Combine(target,"history.json")) || File.Exists(Path.Combine(target,"history.json.deleted"));
            if(File.Exists(pending)) {
                using var state=JsonDocument.Parse(File.ReadAllText(pending));
                if(state.RootElement.ValueKind!=JsonValueKind.Object || !state.RootElement.TryGetProperty("preserveHistory",out var policy) || policy.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new JsonException("Invalid YORU migration plan");
                keepHistory=policy.GetBoolean();
            } else AtomicWrite(pending,JsonSerializer.SerializeToUtf8Bytes(new { preserveHistory=keepHistory }));
            // Share the old history lock while taking the snapshot, including
            // deletion tombstones. Never delete/move the legacy data or overwrite
            // files the user already has in YORU.
            using var history = new Mutex(false, "Local\\YORUHistoryV5");
            try { history.WaitOne(); } catch (AbandonedMutexException) { }
            try {
                if (Directory.Exists(legacy)) {
                    foreach (var source in Directory.EnumerateFiles(legacy,"*",SearchOption.TopDirectoryOnly)) {
                        var name=Path.GetFileName(source);
                        if ((!name.EndsWith(".json",StringComparison.OrdinalIgnoreCase) && name!="history.json.deleted") || name.StartsWith("migration-",StringComparison.OrdinalIgnoreCase)) continue;
                        if (keepHistory && name is "history.json" or "history.json.deleted") continue;
                        CopyIfMissing(source,Path.Combine(target,name));
                    }
                    var compatibility=Path.Combine(legacy,"decoder-safety","compatibility.json");
                    if(File.Exists(compatibility)) CopyIfMissing(compatibility,Path.Combine(target,"decoder-safety","compatibility.json"));
                }
                // Written only after all copies complete. On restart, removed
                // favorites/history must never be reimported from the old app.
                AtomicWrite(marker,JsonSerializer.SerializeToUtf8Bytes(new { version=8,completedUtc=DateTime.UtcNow,legacyFound=Directory.Exists(legacy) }));
            } finally { history.ReleaseMutex(); }
        } finally { migration.ReleaseMutex(); }
    }

    private static void CopyIfMissing(string source,string destination)
    {
        if(File.Exists(destination)) return;
        AtomicWrite(destination,File.ReadAllBytes(source));
    }
    private static void AtomicWrite(string destination,byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temp,bytes); File.Move(temp,destination,false); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
}
