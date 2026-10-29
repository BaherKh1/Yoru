using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Yoru.Controls;

// Visual-only companion to the existing RadioButton/ShowPage navigation.
internal sealed class SidebarMotion : IDisposable
{
    internal const int SlideMilliseconds = 250;
    private readonly FrameworkElement host;
    private readonly Panel items;
    private readonly Border indicator;
    private readonly TranslateTransform translation;
    private readonly RadioButton[] buttons;
    private readonly Dictionary<RadioButton,SolidColorBrush> colors = new();
    private readonly Dictionary<RadioButton,Color> targets = new();
    private bool positioned, disposed;
    private double destination = double.NaN;
    private bool MotionEnabled => SystemParameters.ClientAreaAnimation;
    public SidebarMotion(FrameworkElement host, Panel items, Border indicator)
    {
        this.host=host;this.items=items;this.indicator=indicator;
        translation=(TranslateTransform)indicator.RenderTransform;
        buttons=items.Children.OfType<RadioButton>().ToArray();
        foreach(var button in buttons) {
            var brush=new SolidColorBrush(Color.FromRgb(199,203,222));
            colors.Add(button,brush);button.Foreground=brush;
            button.Checked+=Checked;button.MouseEnter+=Hover;button.MouseLeave+=Hover;
        }
        items.Loaded+=Loaded;items.SizeChanged+=Resized;
        SyncSelection(false);
    }
    internal void SyncSelection(bool animate=true)
    {
        if(disposed) return;
        var selected=buttons.FirstOrDefault(button=>button.IsChecked==true);
        // Radio groups can briefly have no checked item while a page updates selection.
        // Keep the existing visual until the next valid target instead of flashing it off.
        if(selected is null) return;
        foreach(var button in buttons) UpdateColor(button,animate && positioned);
        if(selected.ActualHeight<=0 || items.ActualWidth<=0) return;
        var target=selected.TranslatePoint(new Point(),host).Y-indicator.Margin.Top;
        indicator.Opacity=1;
        // Layout notifications and selection refreshes must not cancel a clock already
        // headed to this row. Only the first placement snaps; later targets retarget smoothly.
        if(positioned && Math.Abs(destination-target)<0.1) return;
        var from=translation.Y;destination=target;
        translation.BeginAnimation(TranslateTransform.YProperty,null);
        translation.Y=target;
        if(positioned && animate && MotionEnabled && Math.Abs(from-target)>0.1) {
            translation.Y=from;
            translation.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(from,target,TimeSpan.FromMilliseconds(SlideMilliseconds)) {
                EasingFunction=new CubicEase { EasingMode=EasingMode.EaseInOut },FillBehavior=FillBehavior.HoldEnd
            },HandoffBehavior.SnapshotAndReplace);
        }
        positioned=true;
    }
    private void UpdateColor(RadioButton button,bool animate)
    {
        var target=button.IsChecked==true ? Color.FromRgb(243,236,255) : button.IsMouseOver ? Color.FromRgb(216,194,255) : Color.FromRgb(199,203,222);
        if(targets.TryGetValue(button,out var previous) && previous==target) return;
        targets[button]=target;
        var brush=colors[button];var from=brush.Color;
        brush.BeginAnimation(SolidColorBrush.ColorProperty,null);brush.Color=target;
        if(animate && MotionEnabled) {
            brush.Color=from;
            brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(from,target,TimeSpan.FromMilliseconds(180)) {
                EasingFunction=new CubicEase { EasingMode=EasingMode.EaseInOut },FillBehavior=FillBehavior.HoldEnd
            },HandoffBehavior.SnapshotAndReplace);
        }
    }
    private void Checked(object sender,RoutedEventArgs e)=>SyncSelection();
    private void Hover(object sender,System.Windows.Input.MouseEventArgs e)=>UpdateColor((RadioButton)sender,positioned);
    private void Loaded(object sender,RoutedEventArgs e)=>SyncSelection();
    private void Resized(object sender,SizeChangedEventArgs e)=>SyncSelection();
    public void Dispose()
    {
        if(disposed) return;disposed=true;
        items.Loaded-=Loaded;items.SizeChanged-=Resized;
        translation.BeginAnimation(TranslateTransform.YProperty,null);
        foreach(var button in buttons) {
            button.Checked-=Checked;button.MouseEnter-=Hover;button.MouseLeave-=Hover;
            colors[button].BeginAnimation(SolidColorBrush.ColorProperty,null);
        }
    }
}
