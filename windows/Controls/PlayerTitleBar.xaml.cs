using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AnimeWitcherDesktop.Controls;
public partial class PlayerTitleBar : UserControl
{
    public PlayerTitleBar() => InitializeComponent();
    private void Minimize_Click(object sender, RoutedEventArgs e) { if(Window.GetWindow(this) is { } host) SystemCommands.MinimizeWindow(host); }
    private void Maximize_Click(object sender, RoutedEventArgs e) { if(Window.GetWindow(this) is { } host) { if(host.WindowState==WindowState.Maximized) SystemCommands.RestoreWindow(host); else SystemCommands.MaximizeWindow(host); } }
    private void Close_Click(object sender, RoutedEventArgs e) { if(Window.GetWindow(this) is { } host) SystemCommands.CloseWindow(host); }
    internal void UpdateState(bool maximized) => MaximizeGlyph.Data = Geometry.Parse(maximized ? "M3,1 L13,1 L13,11 M1,4 L10,4 L10,14 L1,14 Z" : "M1,1 L12,1 L12,12 L1,12 Z");
}
