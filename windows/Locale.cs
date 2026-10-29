using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
namespace AnimeWitcherDesktop;

public sealed class Locale : INotifyPropertyChanged, IDisposable
{
    public static Locale Current { get; } = new();
    private LanguagePreferences preferences = new();
    private FileSystemWatcher? watcher;
    private string language = "en";
    public string Language => language;
    public int Revision { get; private set; }
    public FlowDirection Direction => language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    public TextAlignment Alignment => language == "ar" ? TextAlignment.Right : TextAlignment.Left;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;
    public void Initialize(string? directory = null)
    {
        watcher?.Dispose(); preferences = new(directory); Apply(preferences.Read());
        Directory.CreateDirectory(Path.GetDirectoryName(preferences.FilePath)!);
        watcher = new FileSystemWatcher(Path.GetDirectoryName(preferences.FilePath)!, "preferences.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
        watcher.Changed += Reload; watcher.Created += Reload; watcher.Renamed += Reload;
    }
    private void Reload(object sender, FileSystemEventArgs args)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        _ = dispatcher.BeginInvoke(new Action(() => Apply(preferences.Read())));
    }
    public void SetLanguage(string value)
    {
        preferences.Save(value); Apply(value);
    }
    internal void SetForDiagnostics(string value) => Apply(value);
    private void Apply(string value)
    {
        value = value == "ar" ? "ar" : "en";
        L.Language = value;
        if (language == value && Revision > 0) return;
        language = value; Revision++;
        PropertyChanged?.Invoke(this, new(null)); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() => watcher?.Dispose();
}
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding(nameof(Locale.Revision)) { Source = Locale.Current, Mode = BindingMode.OneWay, Converter = new TranslationConverter(key) }.ProvideValue(serviceProvider);
    private sealed class TranslationConverter(string key) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => L.T(key);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
