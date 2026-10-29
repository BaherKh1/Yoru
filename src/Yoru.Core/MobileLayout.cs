namespace Yoru.Core;
public static class MobileLayout {
    public const int PosterCellWidth=160;
    public static int Columns(int widthDp)=>Math.Clamp(widthDp/PosterCellWidth,2,6);
    // Share Home's sizing target, but retain three compact phone columns and cap
    // cell width rather than the column count on tablets / unfolded windows.
    public static int ColumnsForPage(string page,int widthDp)=>page=="Explore"?Math.Max(3,(int)Math.Ceiling(Math.Max(1,widthDp)/(double)PosterCellWidth)):Columns(widthDp);
    public static int PosterHeight(int widthPixels)=>(int)(widthPixels*1.5);
    // Portrait matches the cinematic reference. Short landscape windows need room for
    // safe-area branding and readable actions rather than overlapping those layers.
    public static int HeroHeight(int widthDp,int heightDp,float safeTopDp=0,float fontScale=1) {
        var portrait=heightDp>=widthDp;
        var readableMinimum=safeTopDp+70+190*Math.Clamp(fontScale,1,2);
        return (int)Math.Max(readableMinimum,portrait?heightDp*.53:heightDp*.88);
    }
    public static int NavigationWidth(int availableWidthDp)=>Math.Min(availableWidthDp-28,640);
    public static int NavigationHeight(float fontScale=1)=>(int)(72+16*(Math.Clamp(fontScale,1,2)-1));
    public static int ContentBottomPadding(int systemBottomDp,float fontScale=1)=>Math.Max(0,systemBottomDp)+30+NavigationHeight(fontScale);
}
