using System.Text.Json;
using System.Text.Json.Nodes;
using Yoru;

internal static class StorageMigrationChecks
{
    internal static void Run()
    {
        var scratch=Path.Combine(Path.GetTempPath(),"yoru-migration-"+Guid.NewGuid().ToString("N"));
        var legacy=Path.Combine(scratch,"Yoru"); Directory.CreateDirectory(legacy);
        var anime=Anime.Parse(new JsonObject { ["name"]="Migration anime" },"retained-anime");
        var episode=Episode.Parse(new JsonObject { ["doc_id"]="003",["name"]="Episode 3" });
        new LanguagePreferences(legacy).Save("ar");
        new HistoryStore(legacy).Save(anime,episode,73.25,1440);
        File.WriteAllText(Path.Combine(legacy,"history.json.deleted"),"{\"removed-anime\":\"2026-01-01T00:00:00Z\"}");
        File.WriteAllText(Path.Combine(legacy,"my-list.json"),JsonSerializer.Serialize(new[] {anime}));
        var originals=Directory.GetFiles(legacy).ToDictionary(p=>Path.GetFileName(p)!,p=>File.ReadAllBytes(p));
        var target=AppStorage.Resolve(scratch);
        Check(target==Path.Combine(scratch,"YORU"),"Storage root was not rebranded");
        foreach(var pair in originals) Check(File.ReadAllBytes(Path.Combine(target,pair.Key!)).SequenceEqual(pair.Value),"Migration changed "+pair.Key);
        Check(new LanguagePreferences(target).Read()=="ar","Migration lost language");
        var history=new HistoryStore(target);
        Check(history.Position(anime.Id,episode.Id)==73.25 && history.Entries.Single().Duration==1440,"Migration lost playback position/duration");
        Check(JsonSerializer.Deserialize<List<Anime>>(File.ReadAllText(Path.Combine(target,"my-list.json")))!.Single().Id==anime.Id,"Migration lost favorites");
        AppStorage.DiagnosticRoot=target;
        try { Check(new LanguagePreferences().Read()=="ar" && new HistoryStore().Position(anime.Id,episode.Id)==73.25,"Default stores ignored the migrated directory"); }
        finally { AppStorage.DiagnosticRoot=null; }
        history.RemoveAnime(anime.Id); File.WriteAllText(Path.Combine(target,"my-list.json"),"[]"); new LanguagePreferences(target).Save("en");
        Check(AppStorage.Resolve(scratch)==target && new HistoryStore(target).Entries.Count==0 && File.ReadAllText(Path.Combine(target,"my-list.json"))=="[]" && new LanguagePreferences(target).Read()=="en","Restart reimported deleted entries or old preferences");
        foreach(var pair in originals) Check(File.ReadAllBytes(Path.Combine(legacy,pair.Key!)).SequenceEqual(pair.Value),"Migration modified legacy source "+pair.Key);

        var collision=Path.Combine(scratch,"collision"); var collisionLegacy=Path.Combine(collision,"Yoru"); var collisionTarget=Path.Combine(collision,"YORU");
        Directory.CreateDirectory(collisionLegacy); Directory.CreateDirectory(collisionTarget);
        foreach(var pair in originals) File.WriteAllBytes(Path.Combine(collisionLegacy,pair.Key!),pair.Value);
        File.WriteAllText(Path.Combine(collisionTarget,"history.json"),"[]"); new LanguagePreferences(collisionTarget).Save("en");
        AppStorage.Resolve(collision);
        Check(File.ReadAllText(Path.Combine(collisionTarget,"history.json"))=="[]" && !File.Exists(Path.Combine(collisionTarget,"history.json.deleted")) && new LanguagePreferences(collisionTarget).Read()=="en","Existing YORU data was overwritten or old tombstones mixed into existing history");

        var interrupted=Path.Combine(scratch,"interrupted"); var old=Path.Combine(interrupted,"Yoru"); var next=Path.Combine(interrupted,"YORU");
        Directory.CreateDirectory(old); Directory.CreateDirectory(next);
        foreach(var pair in originals) File.WriteAllBytes(Path.Combine(old,pair.Key!),pair.Value);
        File.WriteAllBytes(Path.Combine(next,"history.json"),originals["history.json"]);
        File.WriteAllText(Path.Combine(next,"migration-v8.pending.json"),"{\"preserveHistory\":false}");
        AppStorage.Resolve(interrupted);
        Check(File.ReadAllBytes(Path.Combine(next,"history.json.deleted")).SequenceEqual(originals["history.json.deleted"]),"Interrupted migration skipped history deletion markers");

        var failure=Path.Combine(scratch,"failure"); var failedLegacy=Path.Combine(failure,"Yoru"); var failedTarget=Path.Combine(failure,"YORU");
        Directory.CreateDirectory(failedLegacy); Directory.CreateDirectory(Path.Combine(failedTarget,"preferences.json"));
        File.WriteAllBytes(Path.Combine(failedLegacy,"preferences.json"),originals["preferences.json"]);
        Check(AppStorage.Resolve(failure)==failedLegacy && !File.Exists(Path.Combine(failedTarget,"migration-v8.complete.json")),"A failed migration hid the original data or marked itself complete");
        Directory.Delete(Path.Combine(failedTarget,"preferences.json"));
        Check(AppStorage.Resolve(failure)==failedTarget && new LanguagePreferences(failedTarget).Read()=="ar","Failed migration did not safely retry");
        var corrupt=Path.Combine(scratch,"corrupt-plan"); var corruptLegacy=Path.Combine(corrupt,"Yoru"); var corruptTarget=Path.Combine(corrupt,"YORU");
        Directory.CreateDirectory(corruptLegacy); Directory.CreateDirectory(corruptTarget);
        File.WriteAllBytes(Path.Combine(corruptLegacy,"preferences.json"),originals["preferences.json"]);
        File.WriteAllText(Path.Combine(corruptTarget,"migration-v8.pending.json"),"{\"preserveHistory\":\"invalid\"}");
        Check(AppStorage.Resolve(corrupt)==corruptLegacy,"Malformed migration metadata crashed or hid legacy data");
        var fresh=AppStorage.Resolve(Path.Combine(scratch,"fresh"));
        Check(new LanguagePreferences(fresh).Read()=="en","Clean YORU launch is not English");
        Console.WriteLine("PASS: V8 migration preserves settings/favorites/history/progress/tombstones and legacy bytes; default stores, restart deletions, collisions, interrupted-copy recovery, failure fallback/retry and fresh English launch.");
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
}
