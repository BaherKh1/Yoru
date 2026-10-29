namespace Yoru.Core;

public sealed record ViewportAnchor(string RowKey,int Position,int OffsetPixels)
{
    public int Resolve(IReadOnlyList<string> rowKeys)
    {
        for(var i=0;i<rowKeys.Count;i++)if(rowKeys[i]==RowKey)return i;
        return Math.Clamp(Position,0,Math.Max(0,rowKeys.Count-1));
    }
}

// Query/filter-specific, bounded UI bookmarks; offsets remain signed pixels.
public sealed class ViewportBookmarks(int capacity=32)
{
    private readonly Dictionary<string,(ViewportAnchor Anchor,long Stamp)> entries=new(StringComparer.Ordinal);
    private long stamp;
    public void Save(string context,string rowKey,int position,int offsetPixels)
    {
        entries[context]=(new(rowKey,Math.Max(0,position),offsetPixels),++stamp);
        while(entries.Count>Math.Max(1,capacity))entries.Remove(entries.MinBy(pair=>pair.Value.Stamp).Key);
    }
    public ViewportAnchor? Find(string context)
    {
        if(!entries.TryGetValue(context,out var entry))return null;
        entries[context]=(entry.Anchor,++stamp);return entry.Anchor;
    }
    public void Remove(string context)=>entries.Remove(context);
}
