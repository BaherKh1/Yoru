using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using AnimeWitcherDesktop;
namespace Yoru.AndroidApp;
internal static class Ui
{
    internal static readonly Color Background=Color.ParseColor("#090C14"),Panel=Color.ParseColor("#151827"),Accent=Color.ParseColor("#8B5CF6"),Selected=Color.ParseColor("#A78BFA"),Text=Color.ParseColor("#F4F0FC"),Muted=Color.ParseColor("#A6A6BE");
    public static int Dp(Context context,float value)=>(int)(value*context.Resources!.DisplayMetrics!.Density+.5f);
    public static GradientDrawable Shape(Color color,float radius=14){var drawable=new GradientDrawable();drawable.SetColor(color);drawable.SetCornerRadius(radius);return drawable;}
    public static GradientDrawable OutlinedShape(Color color,float radius,Color stroke){var shape=Shape(color,radius);shape.SetStroke(1,stroke);return shape;}
    public static TextView Label(Context context,string text,int size=15,Color? color=null){var view=new TextView(context){Text=text,TextSize=size,Gravity=GravityFlags.CenterVertical,TextDirection=TextDirection.FirstStrong};view.SetTextColor(color??Text);return view;}
    public static Button Button(Context context,string text,Action action,bool primary=false){var view=new Button(context){Text=text,TextSize=13,Background=new RippleDrawable(Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#339A62FF")),Shape(primary?Accent:Panel,Dp(context,12)),null)};view.SetAllCaps(false);view.SetTextColor(Text);view.SetPadding(Dp(context,14),Dp(context,6),Dp(context,14),Dp(context,6));view.Click+=(_,_)=>action();view.ContentDescription=text;return view;}
    public static EditText Search(Context context,string hint){var edit=new EditText(context){Hint=hint,TextSize=14,InputType=Android.Text.InputTypes.ClassText|Android.Text.InputTypes.TextFlagCapSentences,Background=Shape(Panel,Dp(context,14)),TextDirection=TextDirection.FirstStrong};edit.SetSingleLine(true);edit.SetTextColor(Text);edit.SetHintTextColor(Muted);edit.SetPadding(Dp(context,14),0,Dp(context,12),0);return edit;}
    public static LinearLayout Vertical(Context context,int padding=0){var view=new LinearLayout(context){Orientation=Orientation.Vertical};var p=Dp(context,padding);view.SetPadding(p,p,p,p);return view;}
    public static Android.App.AlertDialog.Builder Dialog(Context context,string title){var builder=new Android.App.AlertDialog.Builder(context);builder.SetTitle(title);return builder;}
    public static void Error(Context context,Exception error)=>Toast.MakeText(context,L.Error(error),ToastLength.Long)?.Show();
}
internal sealed class BackAction(Action action) : AndroidX.Activity.OnBackPressedCallback(true){public override void HandleOnBackPressed()=>action();}
internal sealed class InsetsListener : Java.Lang.Object,View.IOnApplyWindowInsetsListener
{
    public WindowInsets OnApplyWindowInsets(View? view,WindowInsets? insets){
        if(view is null || insets is null)return insets!;
        if(OperatingSystem.IsAndroidVersionAtLeast(30)){var edges=insets.GetInsets(WindowInsets.Type.SystemBars()|WindowInsets.Type.DisplayCutout());view.SetPadding(edges.Left,edges.Top,edges.Right,edges.Bottom);}
        else view.SetPadding(insets.SystemWindowInsetLeft,insets.SystemWindowInsetTop,insets.SystemWindowInsetRight,insets.SystemWindowInsetBottom);
        return insets;
    }
}
