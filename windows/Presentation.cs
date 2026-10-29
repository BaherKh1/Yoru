using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace Yoru;

public static class Artwork
{
    public static string[] Alternatives(Anime anime, bool banner = false, string? preferred = null)
    {
        var raw = anime.Raw;
        var posters = banner ? new[] { raw["poster"].Text("large"), anime.Poster, raw.Text("poster_uri") }
            : new[] { anime.Poster, raw["poster"].Text("large"), raw.Text("poster_uri") };
        var covers = new[] { raw.Text("cover_uri"), raw.Text("banner_uri") };
        var urls = new[] { preferred ?? "" }.Concat(banner ? covers.Concat(posters) : posters.Concat(covers));
        var trailer = raw.Text("youtube_video_id");
        // Last resort uses the existing record's trailer, never another anime's artwork.
        if (trailer.Length == 11 && trailer.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            urls = urls.Append("https://i.ytimg.com/vi/" + trailer + "/hqdefault.jpg");
        return urls.Where(Data.WebUrl).SelectMany(url => {
            var uri = new Uri(url);
            // MAL provides JPEG counterparts; WPF does not require a WebP extension.
            return uri.Host == "cdn.myanimelist.net" && uri.AbsolutePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                ? new[] { new UriBuilder(uri) { Path = uri.AbsolutePath[..^5] + ".jpg" }.Uri.AbsoluteUri, url } : new[] { url };
        }).Select(Resolve).Where(url => url.Length > 0).Distinct().ToArray();
    }
    // Used by hidden layout checks to render captured real API images without a network dependency.
    public static string? PreviewDirectory { get; set; }
    public static string Resolve(string url)
    {
        if (!Data.WebUrl(url)) return "";
        if (PreviewDirectory is not null)
        {
            var path = Path.Combine(PreviewDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant() + ".img");
            if (File.Exists(path)) return path;
            return "";
        }
        return url;
    }
}
public sealed class AnimeCardModel : INotifyPropertyChanged
{
    public Anime Source { get; }
    public string Id => Source.Id;
    public string Name => Source.Name;
    private string? poster, cover;
    private string[]? posterFallbacks, coverFallbacks;
    public string[] PosterFallbacks => posterFallbacks ??= Artwork.Alternatives(Source);
    public string[] CoverFallbacks => coverFallbacks ??= Artwork.Alternatives(Source, true);
    public string Poster => poster ??= FirstArtwork(Source.Poster, Source.Raw["poster"].Text("large"), Source.Raw.Text("poster_uri"));
    public string CoverUrl => Source.Raw.Text("cover_uri", Source.Raw.Text("banner_uri", Source.Poster));
    public string Cover => cover ??= FirstArtwork(CoverUrl, Source.Poster, Source.Raw["poster"].Text("large"));
    private static string FirstArtwork(params string[] urls) => urls.Select(Artwork.Resolve).FirstOrDefault(x => x.Length > 0) ?? "";
    public string Story => string.IsNullOrWhiteSpace(Source.Story) ? L.T("StoryFallback") : Source.Story;
    public string Year { get; }
    public string Season { get; }
    public string Type { get; }
    public IReadOnlyList<string> Genres { get; }
    public string Genre => Genres.FirstOrDefault() ?? Type;
    public IReadOnlyList<string> Metadata { get; }
    public string Rating { get; }
    public double RatingValue { get; }
    public bool HasRating => Rating.Length > 0;
    public double Popularity => Source.Raw.Number("views");
    private bool saved;
    public bool IsSaved { get => saved; set { if(saved == value) return; saved = value; PropertyChanged?.Invoke(this, new(nameof(IsSaved))); PropertyChanged?.Invoke(this, new(nameof(SaveLabel))); PropertyChanged?.Invoke(this, new(nameof(SaveAction))); } }
    public string SaveLabel => IsSaved ? L.T("SavedList") : L.T("AddList");
    public string SaveAction => IsSaved ? L.T("Saved") : L.T("AddListAction");
    public AnimeCardModel(Anime anime, bool isSaved = false) {
        Source=anime; saved=isSaved;
        Year=Source.Raw["details"].Text("year", ""); Season=Source.Raw["details"].Text("season"); Type=Source.Raw.Text("type");
        Genres=(Source.Raw["tags"] as JsonArray ?? new()).Select(x=>x?.ToString()??"").Where(x=>x.Length>0).Take(8).ToArray();
        Metadata=new[] { Year,Type }.Concat(Genres.Take(2)).Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray();
        var rate=Source.Raw["details"].Number("mal_mean"); if(rate<=0) rate=Source.Raw["rating"].Number("rate"); if(rate<=0) rate=Source.Raw.Number("average_rate"); RatingValue=rate; Rating=rate>0?rate.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture):"";
    }
    public void RefreshLanguage() => PropertyChanged?.Invoke(this, new(null));
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class ContinueCardModel(WatchEntry entry, Anime anime)
{
    private string[]? imageFallbacks;
    public string[] ImageFallbacks => imageFallbacks ??= Artwork.Alternatives(Source, true, Entry.Cover);
    public WatchEntry Entry { get; } = entry;
    public Anime Source { get; } = anime;
    public string Name => Source.Name;
    public string Episode => Entry.EpisodeName;
    public string Image { get { var cover = Data.WebUrl(Entry.Cover) ? Entry.Cover : Source.Raw.Text("cover_uri"); var image = Artwork.Resolve(cover); return image.Length > 0 ? image : Artwork.Resolve(Source.Poster); } }
    public bool HasProgress => Entry.Duration > 0;
    public double Percent => HasProgress ? Math.Clamp(Entry.Seconds / Entry.Duration * 100, 0, 100) : 0;
    public string Position => Entry.Duration > Entry.Seconds && Entry.Seconds > 0 ? L.T("MinutesLeft", Math.Ceiling((Entry.Duration - Entry.Seconds) / 60)) : Entry.Seconds > 0 ? L.T("Resume", TimeSpan.FromSeconds(Entry.Seconds).ToString(@"mm\:ss")) : L.T("WatchAgain");
}
public sealed class LibraryStore
{
    private readonly string path;
    public List<Anime> Items { get; private set; } = new();
    public LibraryStore(string? storageDirectory = null) { path = Path.Combine(storageDirectory ?? AppStorage.DirectoryPath, "my-list.json"); try { if (File.Exists(path)) Items = JsonSerializer.Deserialize<List<Anime>>(File.ReadAllText(path)) ?? new(); } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { } }
    public bool Contains(string id) => Items.Any(x => x.Id == id);
    public void Toggle(Anime anime)
    {
        if (Contains(anime.Id)) Items.RemoveAll(x => x.Id == anime.Id); else Items.Insert(0, anime);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Items)); File.Move(path + ".tmp", path, true);
    }
}
// Rows recycle in the catalog; each row uses the unchanged V4 panel geometry.
public sealed class CatalogRow(IEnumerable<AnimeCardModel> items, bool last) : List<AnimeCardModel>(items), INotifyPropertyChanged
{
    private bool isLast=last;
    public Thickness RowMargin => new(0,0,0,isLast?0:20);
    public void SetLast(bool value) { if(isLast==value) return; isLast=value; PropertyChanged?.Invoke(this,new(nameof(RowMargin))); }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class ContinueRow(IEnumerable<ContinueCardModel> items,bool last):List<ContinueCardModel>(items)
{
    public Thickness RowMargin=>new(0,0,0,last?0:20);
}
public class ResponsiveCardPanel : Panel
{
    public static readonly DependencyProperty MinCardWidthProperty = DependencyProperty.Register(nameof(MinCardWidth), typeof(double), typeof(ResponsiveCardPanel), new FrameworkPropertyMetadata(144d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(nameof(MaxColumns), typeof(int), typeof(ResponsiveCardPanel), new FrameworkPropertyMetadata(20, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(nameof(AspectRatio), typeof(double), typeof(ResponsiveCardPanel), new FrameworkPropertyMetadata(0.72d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty FooterHeightProperty = DependencyProperty.Register(nameof(FooterHeight), typeof(double), typeof(ResponsiveCardPanel), new FrameworkPropertyMetadata(64d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double MinCardWidth { get => (double)GetValue(MinCardWidthProperty); set => SetValue(MinCardWidthProperty, value); }
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    public double AspectRatio { get => (double)GetValue(AspectRatioProperty); set => SetValue(AspectRatioProperty, value); }
    public double FooterHeight { get => (double)GetValue(FooterHeightProperty); set => SetValue(FooterHeightProperty, value); }
    internal const double Gap = 16;
    internal static int ColumnsFor(double space, double minWidth = 145, int maxColumns = 20) => Math.Clamp((int)((space + Gap) / (minWidth + Gap)), 1, Math.Max(1, maxColumns));
    private int columns;
    private double width, height;
    protected override Size MeasureOverride(Size available)
    {
        var space = double.IsInfinity(available.Width) ? 1100 : Math.Max(1, available.Width);
        columns = ColumnsFor(space, MinCardWidth, MaxColumns);
        width = Math.Max(1, (space - (columns - 1) * Gap) / columns); height = width / AspectRatio + FooterHeight;
        foreach (UIElement child in InternalChildren) child.Measure(new Size(width, height));
        var rows = (int)Math.Ceiling(InternalChildren.Count / (double)columns);
        return new(space, rows == 0 ? 0 : rows * height + (rows - 1) * 20);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++) InternalChildren[i].Arrange(new Rect((i % columns) * (width + Gap), (i / columns) * (height + 20), width, height));
        return finalSize;
    }
}
