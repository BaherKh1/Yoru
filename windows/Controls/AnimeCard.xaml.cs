using System.Windows;
using System.Windows.Controls;
namespace Yoru.Controls;
public partial class AnimeCard : UserControl
{
    public static readonly RoutedEvent SelectedEvent = EventManager.RegisterRoutedEvent(nameof(Selected), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AnimeCard));
    public static readonly RoutedEvent SaveRequestedEvent = EventManager.RegisterRoutedEvent(nameof(SaveRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AnimeCard));
    public event RoutedEventHandler Selected { add => AddHandler(SelectedEvent, value); remove => RemoveHandler(SelectedEvent, value); }
    public event RoutedEventHandler SaveRequested { add => AddHandler(SaveRequestedEvent, value); remove => RemoveHandler(SaveRequestedEvent, value); }
    public AnimeCard() => InitializeComponent();
    private void Select_Click(object sender, RoutedEventArgs e) { e.Handled = true; RaiseEvent(new RoutedEventArgs(SelectedEvent, this)); }
    private void Save_Click(object sender, RoutedEventArgs e) { e.Handled = true; RaiseEvent(new RoutedEventArgs(SaveRequestedEvent, this)); }
}
