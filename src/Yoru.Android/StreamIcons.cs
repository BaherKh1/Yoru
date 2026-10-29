using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace Yoru.AndroidApp;

// One 24dp stroke grid for navigation, actions and grouped settings rows.
internal sealed class StreamIcon(string name, Color color) : Drawable
{
    private readonly Paint pen = new(PaintFlags.AntiAlias) { Color = color, StrokeWidth = 1.8f, StrokeCap = Paint.Cap.Round, StrokeJoin = Paint.Join.Round };
    public void Tint(Color value) { pen.Color = value; InvalidateSelf(); }
    public override void Draw(Canvas canvas)
    {
        canvas.Save(); canvas.Translate(Bounds.Left, Bounds.Top); canvas.Scale(Bounds.Width() / 24f, Bounds.Height() / 24f);
        pen.SetStyle(Paint.Style.Stroke);
        void Line(float x,float y,float a,float b)=>canvas.DrawLine(x,y,a,b,pen);
        void Path(params float[] points) { using var p=new Android.Graphics.Path();p.MoveTo(points[0],points[1]);for(var i=2;i<points.Length;i+=2)p.LineTo(points[i],points[i+1]);canvas.DrawPath(p,pen); }
        switch(name) {
            case "Home": Path(3,10,12,3,21,10,21,21,15,21,15,14,9,14,9,21,3,21,3,10); break;
            case "Explore": canvas.DrawCircle(12,12,9,pen);Path(16,8,14,14,8,16,10,10,16,8);break;
            case "Chart": Path(3,17,9,11,13,14,21,5);Path(15,5,21,5,21,11);break;
            case "History": canvas.DrawCircle(12,12,9,pen);Path(12,7,12,12,16,14);break;
            case "More": pen.SetStyle(Paint.Style.Fill);foreach(var x in new[]{5,12,19})canvas.DrawCircle(x,12,1.7f,pen);break;
            case "Search": canvas.DrawCircle(10.5f,10.5f,6.5f,pen);Line(15.5f,15.5f,21,21);break;
            case "MyList": Path(6,3,18,3,18,21,12,17,6,21,6,3);break;
            case "Genres": foreach(var x in new[]{3,14})foreach(var y in new[]{3,14})canvas.DrawRoundRect(x,y,x+7,y+7,1.5f,1.5f,pen);break;
            case "Seasonal": canvas.DrawRoundRect(3,5,21,21,3,3,pen);Line(3,10,21,10);Line(8,3,8,7);Line(16,3,16,7);break;
            case "Settings": canvas.DrawCircle(12,12,4,pen);for(var i=0;i<8;i++){canvas.Save();canvas.Rotate(i*45,12,12);Line(12,2,12,5);canvas.Restore();}canvas.DrawCircle(12,12,8,pen);break;
            case "Chevron": Path(9,5,16,12,9,19);break;
            case "Back": Path(15,5,8,12,15,19);break;
            case "Server": foreach(var y in new[]{3,13}){canvas.DrawRoundRect(3,y,21,y+7,2,2,pen);Line(7,y+3.5f,8,y+3.5f);Line(12,y+3.5f,17,y+3.5f);}break;
            case "Download": case "Downloads": Path(12,3,12,15);Path(6,10,12,16,18,10);Path(4,17,4,21,20,21,20,17);break;
            case "Fullscreen": Path(3,9,3,3,9,3);Path(15,3,21,3,21,9);Path(21,15,21,21,15,21);Path(9,21,3,21,3,15);break;
            case "Previous": Line(5,5,5,19);Path(19,5,8,12,19,19,19,5);break;
            case "Next": Line(19,5,19,19);Path(5,5,16,12,5,19,5,5);break;
            case "Check": Path(5,12,10,17,20,6);break;
            case "Play": pen.SetStyle(Paint.Style.Fill);using(var triangle=new Android.Graphics.Path()){triangle.MoveTo(8,4);triangle.LineTo(21,12);triangle.LineTo(8,20);triangle.Close();canvas.DrawPath(triangle,pen);}break;
            case "Remove": canvas.DrawRoundRect(6,7,18,21,2,2,pen);Line(4,7,20,7);Path(9,7,9,3,15,3,15,7);Line(10,11,10,17);Line(14,11,14,17);break;
            default: Line(5,12,19,12);Line(12,5,12,19);break;
        }
        canvas.Restore();
    }
    public override int Opacity => (int)Format.Translucent;
    public override void SetAlpha(int alpha) { pen.Alpha=alpha;InvalidateSelf(); }
    public override void SetColorFilter(ColorFilter? filter) { pen.SetColorFilter(filter);InvalidateSelf(); }
}

internal static class StreamActions
{
    public static ImageView Icon(Context context,string name,Color? color=null,int size=24) {
        var image=new ImageView(context);image.SetImageDrawable(new StreamIcon(name,color??Ui.Muted));image.SetScaleType(ImageView.ScaleType.FitCenter);image.LayoutParameters=new LinearLayout.LayoutParams(Ui.Dp(context,size),Ui.Dp(context,size));return image;
    }
    public static ImageButton Button(Context context,string name,string label,Action action) {
        var button=new ImageButton(context){ContentDescription=label,Background=new RippleDrawable(Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#338B5CF6")),Ui.Shape(Color.ParseColor("#66101522"),Ui.Dp(context,24)),null)};
        button.SetImageDrawable(new StreamIcon(name,Ui.Text));var p=Ui.Dp(context,12);button.SetPadding(p,p,p,p);button.Click+=(_,_)=>action();return button;
    }
    public static void ButtonIcon(Button button,string name) {
        var icon=new StreamIcon(name,Ui.Text);var side=Ui.Dp(button.Context!,18);icon.SetBounds(0,0,side,side);button.SetCompoundDrawablesRelative(icon,null,null,null);button.CompoundDrawablePadding=Ui.Dp(button.Context!,7);
    }
}
