namespace Yoru.Core;

public static class EpisodeNavigation
{
    public static int? Target(int current,int count,int delta)=>current>=0 && current<count && delta is -1 or 1 && current+delta>=0 && current+delta<count?current+delta:null;
}
