using Yoru;
namespace Yoru.Core;

public sealed record AnimeDownloads(Anime Anime,IReadOnlyList<EpisodeDownload> Episodes,int Completed,int Active,long Bytes,int? Percent,int Failed,int Cancelled,int Interrupted);
public static class DownloadGroups
{
    public static IReadOnlyList<AnimeDownloads> Build(IEnumerable<EpisodeDownload> downloads)=>downloads.GroupBy(item=>item.Anime.Id).OrderBy(group=>group.First().Anime.Name,StringComparer.CurrentCulture).Select(group=>{
        var rows=group.OrderBy(item=>item.EpisodeOrder).ThenBy(item=>item.Episode.Id,StringComparer.Ordinal).ToArray();var active=rows.Where(item=>item.Active).ToArray();
        int? percent=null;if(active.Length>0 && active.All(item=>item.Percent.HasValue)){
            if(active.All(item=>item.Total>0)){var total=active.Sum(item=>item.Total!.Value);percent=(int)Math.Clamp(active.Sum(item=>item.Percent!.Value*(double)item.Total!.Value)/total,0,100);}
            else percent=(int)Math.Clamp(active.Average(item=>item.Percent!.Value),0,100);
        }
        return new AnimeDownloads(rows[0].Anime,Array.AsReadOnly(rows),rows.Count(item=>item.Status==DownloadStatus.Completed),active.Length,rows.Where(item=>item.Status==DownloadStatus.Completed || item.Active).Sum(item=>item.Bytes),percent,rows.Count(item=>item.Status==DownloadStatus.Failed),rows.Count(item=>item.Status==DownloadStatus.Cancelled),rows.Count(item=>item.Status==DownloadStatus.Interrupted));
    }).ToArray();
}
