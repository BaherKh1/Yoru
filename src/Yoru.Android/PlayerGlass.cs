using Android.App;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;
using AnimeWitcherDesktop;

namespace Yoru.AndroidApp;

// Lightweight translucent surfaces; never blur the video/controls view tree or copy video frames.
internal static class PlayerGlass
{
    public static Drawable Surface(Activity owner,bool selected=false,int radius=16){
        var glass=Ui.OutlinedShape(Color.Transparent,Ui.Dp(owner,radius),Color.ParseColor(selected?"#9FAD88FF":"#40D9CCFF"));
        glass.SetOrientation(GradientDrawable.Orientation.TopBottom);
        glass.SetColors(new[]{Color.ParseColor(selected?"#678B5CF6":"#B52A283B").ToArgb(),Color.ParseColor(selected?"#443F286B":"#B5121622").ToArgb()});
        return new RippleDrawable(ColorStateList.ValueOf(Color.ParseColor("#388B5CF6")),glass,null);
    }

    public static ImageButton Icon(Activity owner,string icon,string label,Action action){
        var button=StreamActions.Button(owner,icon,label,action);button.Background=Surface(owner);button.Elevation=Ui.Dp(owner,3);return button;
    }
    public static Button Action(Activity owner,string icon,string label,Action action,bool selected=false){
        var button=Ui.Button(owner,label,action);button.Background=Surface(owner,selected);button.TextSize=12;button.SetMinimumHeight(Ui.Dp(owner,48));button.SetMinHeight(Ui.Dp(owner,48));
        button.SetMaxLines(1);button.Ellipsize=Android.Text.TextUtils.TruncateAt.End;StreamActions.ButtonIcon(button,icon);button.Elevation=Ui.Dp(owner,2);return button;
    }

    public static Dialog Show(Activity owner,string heading,Action<LinearLayout,Action> populate){
        var dialog=new Dialog(owner);dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);
        var panel=Ui.Vertical(owner,16);panel.LayoutDirection=L.Language=="ar"?LayoutDirection.Rtl:LayoutDirection.Ltr;
        var header=new LinearLayout(owner){Orientation=Orientation.Horizontal};header.SetGravity(GravityFlags.CenterVertical);
        var title=Ui.Label(owner,heading,17);title.SetTypeface(null,TypefaceStyle.Bold);title.SetMaxLines(2);title.Ellipsize=Android.Text.TextUtils.TruncateAt.End;
        header.AddView(title,new LinearLayout.LayoutParams(0,-2,1));
        var close=Ui.Button(owner,L.T("MobileCancel"),dialog.Dismiss);close.Background=Surface(owner);close.TextSize=11;
        header.AddView(close,new LinearLayout.LayoutParams(-2,Ui.Dp(owner,48)){MarginStart=Ui.Dp(owner,12)});panel.AddView(header);
        var rows=Ui.Vertical(owner);populate(rows,dialog.Dismiss);
        var scroll=new BoundedScroll(owner){FillViewport=false};scroll.ScrollBarStyle=ScrollbarStyles.OutsideOverlay;scroll.AddView(rows);panel.AddView(scroll,new LinearLayout.LayoutParams(-1,-2){TopMargin=Ui.Dp(owner,12)});
        dialog.SetContentView(panel);dialog.SetCanceledOnTouchOutside(true);
        var window=dialog.Window!;window.SetBackgroundDrawable(Ui.OutlinedShape(Color.ParseColor("#DF121521"),Ui.Dp(owner,24),Color.ParseColor("#55A89CC7")));
        window.AddFlags(WindowManagerFlags.DimBehind);window.SetDimAmount(.28f);window.SetWindowAnimations(Resource.Style.PlayerGlassAnimation);
        if(OperatingSystem.IsAndroidVersionAtLeast(31)){
            // Cross-window blur is supported by Android 12+ and may be disabled by the OS.
            // The dark translucent surface remains readable when blur is unavailable.
            window.SetBackgroundBlurRadius(Ui.Dp(owner,18));window.AddFlags(WindowManagerFlags.BlurBehind);
            var attributes=window.Attributes!;attributes.BlurBehindRadius=Ui.Dp(owner,12);window.Attributes=attributes;
        }
        dialog.Show();window.SetLayout(Math.Min(Ui.Dp(owner,440),owner.Resources!.DisplayMetrics!.WidthPixels-Ui.Dp(owner,32)),-2);
        return dialog;
    }
    public static void Row(Activity owner,LinearLayout parent,string icon,string label,string detail,Action action,bool selected=false,bool enabled=true){
        var row=new LinearLayout(owner){Orientation=Orientation.Horizontal,Background=Surface(owner,selected),Clickable=enabled,Focusable=enabled,Enabled=enabled,Alpha=enabled?1:.35f};
        row.SetGravity(GravityFlags.CenterVertical);row.SetPadding(Ui.Dp(owner,14),Ui.Dp(owner,10),Ui.Dp(owner,14),Ui.Dp(owner,10));row.SetMinimumHeight(Ui.Dp(owner,56));
        row.AddView(StreamActions.Icon(owner,icon,selected?Ui.Selected:Ui.Text),new LinearLayout.LayoutParams(Ui.Dp(owner,24),Ui.Dp(owner,24)){MarginEnd=Ui.Dp(owner,12)});
        var text=Ui.Vertical(owner);var name=Ui.Label(owner,label,14,selected?Ui.Selected:Ui.Text);name.SetMaxLines(2);name.Ellipsize=Android.Text.TextUtils.TruncateAt.End;text.AddView(name);
        if(detail.Length>0){var subtitle=Ui.Label(owner,detail,11,Ui.Muted);subtitle.SetMaxLines(2);subtitle.Ellipsize=Android.Text.TextUtils.TruncateAt.End;text.AddView(subtitle);}
        row.AddView(text,new LinearLayout.LayoutParams(0,-2,1));if(selected)row.AddView(StreamActions.Icon(owner,"Check",Ui.Selected,20));
        row.ContentDescription=label+(detail.Length>0?", "+detail:"");row.Selected=selected;if(enabled)row.Click+=(_,_)=>action();
        parent.AddView(row,new LinearLayout.LayoutParams(-1,-2){TopMargin=Ui.Dp(owner,4),BottomMargin=Ui.Dp(owner,4)});
    }
    private sealed class BoundedScroll(Activity owner):ScrollView(owner){
        protected override void OnMeasure(int widthMeasureSpec,int heightMeasureSpec){
            var limit=(int)(owner.Resources!.DisplayMetrics!.HeightPixels*.55);
            base.OnMeasure(widthMeasureSpec,MeasureSpec.MakeMeasureSpec(Math.Min(MeasureSpec.GetSize(heightMeasureSpec),limit),MeasureSpecMode.AtMost));
        }
    }
}
