using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Yoru.Controls;
public partial class ContinueCard : UserControl
{
    public static readonly RoutedEvent ResumeRequestedEvent = EventManager.RegisterRoutedEvent(nameof(ResumeRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ContinueCard));
    public event RoutedEventHandler ResumeRequested { add => AddHandler(ResumeRequestedEvent, value); remove => RemoveHandler(ResumeRequestedEvent, value); }
    public static readonly RoutedEvent RemoveRequestedEvent = EventManager.RegisterRoutedEvent(nameof(RemoveRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ContinueCard));
    public event RoutedEventHandler RemoveRequested { add => AddHandler(RemoveRequestedEvent, value); remove => RemoveHandler(RemoveRequestedEvent, value); }
    private void Menu_Click(object sender, RoutedEventArgs e) { var button = (Button)sender; button.ContextMenu.PlacementTarget = button; button.ContextMenu.IsOpen = true; e.Handled = true; }
    private void Remove_Click(object sender, RoutedEventArgs e) { e.Handled = true; RaiseEvent(new RoutedEventArgs(RemoveRequestedEvent, this)); }
    public ContinueCard()
    {
        InitializeComponent();
        // UniformToFill artwork can render beyond its layout slot. Clip the complete
        // card in card coordinates, including the artwork, overlay and progress bar.
        ArtworkSurface.SizeChanged += (_, _) => ArtworkSurface.Clip = new RectangleGeometry(new Rect(ArtworkSurface.RenderSize), 9, 9);
        CardSurface.SizeChanged += (_, _) => CardSurface.Clip = new RectangleGeometry(new Rect(CardSurface.RenderSize), 10, 10);
    }
    private void Resume_Click(object sender, RoutedEventArgs e) { e.Handled = true; RaiseEvent(new RoutedEventArgs(ResumeRequestedEvent, this)); }
}
