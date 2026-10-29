using System.Text.Json.Nodes;
using AnimeWitcherDesktop;
using Yoru.Core;

internal static class ViewportBookmarkChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static async Task Run()
    {
        var bookmarks=new ViewportBookmarks();var context="Explore\0Jujutsu\0default";
        var rows=new[]{"catalog-title","sort"}.Concat(Enumerable.Range(0,100).Select(i=>"anime:"+i)).ToArray();
        bookmarks.Save(context,"anime:75",77,-139);
        var anchor=bookmarks.Find(context)!;
        Check(anchor.Resolve(rows)==77 && anchor.OffsetPixels==-139,"Partial-row pixel offset was lost");
        var moved=new[]{"catalog-title","filter","sort"}.Concat(rows.Skip(2)).ToArray();
        Check(anchor.Resolve(moved)==78,"Stable anchor did not follow its anime after rows moved");
        bookmarks.Save("Explore\0Jujutsu\0rating","anime:12",14,-47);
        bookmarks.Save("Explore\0\u062c\u0648\u062c\u0648\0default","anime:8",10,18);
        Check(bookmarks.Find(context)!.OffsetPixels==-139 && bookmarks.Find("Explore\0Jujutsu\0rating")!.OffsetPixels==-47,"Search/sort bookmarks overwrite one another");
        Check(bookmarks.Find("Explore\0\u062c\u0648\u062c\u0648\0default")!.OffsetPixels==18,"Positive offsets or Arabic query identity were lost");
        Check(anchor.Resolve(new[]{"header","other"})==1 && anchor.Resolve(Array.Empty<string>())==0,"Removed-item fallback is out of bounds");
        bookmarks.Remove(context);Check(bookmarks.Find(context) is null,"Explicit tab-to-top did not discard its old bookmark");

        var bounded=new ViewportBookmarks(2);bounded.Save("A","a",1,-1);bounded.Save("B","b",2,-2);bounded.Find("A");bounded.Save("C","c",3,-3);
        Check(bounded.Find("A") is not null && bounded.Find("B") is null && bounded.Find("C") is not null,"Bookmark capacity/recency handling failed");

        // A saved viewport resolves against the same multi-page session; returning
        // needs no Search/Load invocation and retains ordering and NextPage.
        var fetches=0;
        Anime Anime(int id)=>AnimeWitcherDesktop.Anime.Parse(new JsonObject{["objectID"]=id.ToString(),["name"]="Anime "+id});
        var catalog=new CatalogSession("query",(_,page,_)=>{fetches++;return Task.FromResult(new SearchPage(Enumerable.Range(page*30,30).Select(Anime).ToList(),page,4,120));});
        await catalog.Load();await catalog.Load();await catalog.Load();
        var retained=catalog;var before=fetches;var ids=retained.Items.Select(a=>"anime:"+a.Id).ToArray();
        bookmarks.Save("return","anime:75",75,-211);
        var restored=bookmarks.Find("return")!;
        Check(restored.Resolve(ids)==75 && restored.OffsetPixels==-211 && retained.Items.Count==90 && retained.NextPage==3 && fetches==before,"Restoring a multi-page catalog lost data, offset or paging state");
    }
}
