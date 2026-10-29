using System.Text.Json.Nodes;
using Yoru;
using Yoru.Core;
internal static class DownloadGroupChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run(){
        var a=Anime.Parse(new JsonObject{["objectID"]="a",["name"]="Anime A"});var b=a with{Id="b",Name="Anime B"};var ep=new Episode("1","Episode 1","",new());
        EpisodeDownload Job(Anime anime,int episode,DownloadStatus status,long bytes,long? total=null,int? percent=null)=>new(DownloadLibrary.Key(anime.Id,episode.ToString()),anime,ep with{Id=episode.ToString()},episode,"KF","720p","https://fixture.invalid/source","source","720p",status,bytes,total,percent);
        var jobs=new[]{Job(a,1,DownloadStatus.Completed,1000),Job(a,2,DownloadStatus.Downloading,500,1000,50),Job(a,3,DownloadStatus.Downloading,250,1000,25),Job(a,4,DownloadStatus.Failed,900),Job(a,5,DownloadStatus.Cancelled,900),Job(b,1,DownloadStatus.Interrupted,800)};
        var groups=DownloadGroups.Build(jobs);Check(groups.Count==2,"Episodes were not grouped by anime identity");var group=groups[0];
        Check(group.Completed==1 && group.Active==2 && group.Bytes==1750 && group.Percent==37,"Grouped episode/size/progress values include deleted partials or are inaccurate");
        Check(group.Failed==1 && group.Cancelled==1 && groups[1].Interrupted==1,"Failed/cancelled/interrupted states were lost");
        Check(group.Episodes.Select(item=>item.EpisodeOrder).SequenceEqual(new[]{1,2,3,4,5}),"Expanded episode order is unstable");
        var unknown=DownloadGroups.Build(jobs.Append(Job(a,6,DownloadStatus.Queued,0))).First();Check(unknown.Percent is null && unknown.Active==3,"Unknown-size queued transfers show a fabricated percentage");
        Check(DownloadGroups.Build(new[]{Job(a,9,DownloadStatus.Downloading,20,1000000,50)}).Single().Percent==50,"Estimated HLS size replaced the transfer's actual object progress");
        var completed=DownloadGroups.Build(jobs.Select(item=>item.Status==DownloadStatus.Downloading?item with{Status=DownloadStatus.Completed,Bytes=1000,Percent=100}:item)).First();Check(completed.Active==0 && completed.Percent is null && completed.Completed==3,"Completed anime retains an active progress indicator");
        Check(DownloadGroups.Build(Array.Empty<EpisodeDownload>()).Count==0,"Deleted groups remain in the Downloads page");
        foreach(var width in new[]{320,360,393,412,480})Check(MobileLayout.ColumnsForPage("Explore",width)==3,"Regular phones lost their three-column Explore layout");
        foreach(var width in new[]{540,600,720,800,1024,1280,1600,1920,2560}){
            var columns=MobileLayout.ColumnsForPage("Explore",width);Check(columns>3 && width/(double)columns<=MobileLayout.PosterCellWidth,"A wide / unfolded container oversized Explore posters");
            foreach(var page in new[]{"Home","Genres","Seasonal","MyList"})Check(MobileLayout.ColumnsForPage(page,width)==MobileLayout.Columns(width),"Non-Explore grid dimensions changed");
        }
        Check(MobileLayout.ColumnsForPage("Explore",412)==3 && MobileLayout.ColumnsForPage("Explore",800)==5 && MobileLayout.ColumnsForPage("Explore",1920)==12,"Container resize breakpoints are inconsistent");
    }
}
