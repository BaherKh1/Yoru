using System.Globalization;
using System.Reflection;
using System.Text.Json;
namespace AnimeWitcherDesktop;

public static class L
{
    private static readonly Dictionary<string, string[]> values = Load();
    public static string Language { get; internal set; } = "en";
    public static IReadOnlyDictionary<string, string[]> Entries => values;
    private static Dictionary<string, string[]> Load()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("AnimeWitcherDesktop.Localization.strings.json") ?? throw new InvalidOperationException("Translation resource missing");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!;
    }
    public static string KeyFor(string text) => values.ContainsKey(text) ? text : values.FirstOrDefault(x => x.Value.Contains(text)).Key ?? text;
    public static string T(string key, params object[] args)
    {
        key = KeyFor(key);
        var text = values.TryGetValue(key, out var pair) ? pair[Language == "ar" ? 1 : 0] : key;
        return args.Length == 0 ? text : string.Format(CultureInfo.GetCultureInfo(Language), text, args);
    }
    public static string Error(Exception error)
    {
        var key = KeyFor(error.Message); if (values.ContainsKey(key)) return T(key);
        if (error is System.Net.Http.HttpRequestException http && http.StatusCode is { } status) return T((int)status switch { 401 or 403 => "ServiceDenied", 404 => "ItemMissing", 429 => "ServiceBusy", _ => "ServiceHttp" }, (int)status);
        if (error is PlaybackException) return error.Message;
        return T(error is System.Net.Http.HttpRequestException ? "NetworkError" : "GenericError");
    }
}
public class UiException(string key, params object[] args) : Exception
{
    public override string Message => L.T(key, args);
}
public sealed class LocalizedHttpException(string key, System.Net.HttpStatusCode status, params object[] args) : System.Net.Http.HttpRequestException(null, null, status)
{
    public override string Message => L.T(key, args);
}
