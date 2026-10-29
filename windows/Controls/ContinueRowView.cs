using System.Windows;

namespace Yoru.Controls;

public sealed class ContinueRowView : ResponsiveCardPanel
{
    public event RoutedEventHandler ResumeRequested { add=>AddHandler(ContinueCard.ResumeRequestedEvent,value); remove=>RemoveHandler(ContinueCard.ResumeRequestedEvent,value); }
    public event RoutedEventHandler RemoveRequested { add=>AddHandler(ContinueCard.RemoveRequestedEvent,value); remove=>RemoveHandler(ContinueCard.RemoveRequestedEvent,value); }
    public ContinueRowView()
    {
        MinCardWidth=250; MaxColumns=5; AspectRatio=1.75; FooterHeight=0;
        DataContextChanged+=(_,_)=> {
            if(DataContext is not ContinueRow row) { foreach(ContinueCard card in Children) card.DataContext=null; return; }
            while(Children.Count<row.Count) Children.Add(new ContinueCard());
            while(Children.Count>row.Count) Children.RemoveAt(Children.Count-1);
            for(int i=0;i<row.Count;i++) ((ContinueCard)Children[i]).DataContext=row[i];
        };
    }
}
