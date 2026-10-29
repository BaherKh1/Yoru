using System.Text.RegularExpressions;
using Yoru;
namespace Yoru.Core;

public sealed record QualityServers(string Quality,IReadOnlyList<VideoServer> Servers);
public static class ServerGroups
{
    public static string Key(VideoServer server)=>server.Name+"\0"+server.Quality+"\0"+server.Link;
    public static string Quality(string value){var match=Regex.Match(value.Trim(),@"^(\d{3,4})\s*[pP]?$");return match.Success?match.Groups[1].Value+"p":value.Trim();}
    public static IReadOnlyList<QualityServers> Group(IEnumerable<VideoServer> servers)=>servers.DistinctBy(Key).GroupBy(server=>Quality(server.Quality),StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group=>int.TryParse(group.Key.TrimEnd('p','P'),out var height)?height:0).ThenBy(group=>group.Key,StringComparer.OrdinalIgnoreCase)
        .Select(group=>new QualityServers(group.Key,Array.AsReadOnly(group.ToArray()))).ToArray();
}
