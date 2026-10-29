using System.Windows;

namespace Yoru.Controls;

// Recycling ListBox rows should also recycle their six/eight poster controls.
// A nested ItemsControl would destroy/recreate every poster on each row rebind.
public sealed class CatalogRowView : ResponsiveCardPanel
{
    public event RoutedEventHandler Selected { add=>AddHandler(AnimeCard.SelectedEvent,value); remove=>RemoveHandler(AnimeCard.SelectedEvent,value); }
    public event RoutedEventHandler SaveRequested { add=>AddHandler(AnimeCard.SaveRequestedEvent,value); remove=>RemoveHandler(AnimeCard.SaveRequestedEvent,value); }
    public CatalogRowView()
    {
        MinCardWidth=145; AspectRatio=0.7; FooterHeight=62;
        DataContextChanged+=(_,_)=>BindCards();
    }
    private void BindCards()
    {
        if(DataContext is not CatalogRow row) { foreach(AnimeCard card in Children) card.DataContext=null; return; }
        while(Children.Count<row.Count) Children.Add(new AnimeCard());
        while(Children.Count>row.Count) Children.RemoveAt(Children.Count-1);
        for(int i=0;i<row.Count;i++) ((AnimeCard)Children[i]).DataContext=row[i];
    }
}
