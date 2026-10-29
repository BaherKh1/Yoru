namespace Yoru.Core;

// Fixed section hierarchy also survives Activity recreation (for example after
// a language change), without depending on a transient previous-page variable.
public static class MobileNavigation
{
    public static IReadOnlyList<string> Tabs{get;}=Array.AsReadOnly(new[]{"Home","Explore","History","More"});
    public static string NormalizePage(string page)=>page=="Trending"?"Explore":page;
    public static bool IsMoreChild(string page)=>page is "MyList" or "Genres" or "Seasonal" or "Settings" or "Downloads";
    public static string SelectedTab(string page,string detailsParent="Home")
    {
        var section=NormalizePage(page=="Details"?detailsParent:page);
        return IsMoreChild(section)?"More":section;
    }
    public static string? BackTarget(string page,string detailsParent="Home")=>NormalizePage(page) switch
    {
        "Home"=>null,
        "Details"=>NormalizePage(detailsParent),
        _ when IsMoreChild(page)=>"More",
        _=>"Home"
    };
}
