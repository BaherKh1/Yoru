using Android.Content;
using Android.Util;
using AndroidX.RecyclerView.Widget;

namespace Yoru.AndroidApp;

// RecyclerView drives each frame and cancels this naturally on a user drag.
// Shared by Home and Explore. SnapToStart aligns the page's exact first-row edge.
internal sealed class HomeSmoothScroller(Context context):LinearSmoothScroller(context)
{
    protected override int VerticalSnapPreference=>SnapToStart;
    protected override float CalculateSpeedPerPixel(DisplayMetrics? metrics)=>32f/Math.Max(1,metrics is null?160:(int)metrics.DensityDpi);
    protected override int CalculateTimeForDeceleration(int dx)=>Math.Clamp(base.CalculateTimeForDeceleration(dx),120,500);
}
