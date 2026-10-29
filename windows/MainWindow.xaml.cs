using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AnimeWitcherDesktop.Controls;

namespace AnimeWitcherDesktop;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly SidebarMotion sidebarMotion;
    private readonly WitcherApi api = new();
    private readonly LibraryStore library;
    private readonly ApiDataCache dataCache;
    private readonly HistoryStore history;
    private readonly System.Windows.Threading.DispatcherTimer historyPoll = new() { Interval = TimeSpan.FromSeconds(2) };
    private DateTime historyStamp;
    private CancellationTokenSource? operation;
    private SearchPage? results;
    private List<AnimeCardModel> catalog = new(), collection = new(), features = new();
    private List<Episode> episodes = new();
    private Anime? currentAnime;
    private Episode? currentEpisode;
    private AnimeCardModel? featured;
    private string page = "Home", returnPage = "Home", query = "", selectedGenre = "";
    private int featureIndex;
    private string featureDotsState="";
    private bool loadingMore;
    private readonly System.Windows.Threading.DispatcherTimer catalogPump = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private DateTime catalogRetryAt;
    private int catalogFailures, rowColumns;
    private List<AnimeCardModel> visibleCatalog = new();
    private bool reflowing,reflowScheduled;
    private ObservableCollection<CatalogRow> catalogRows = new();
    private List<AnimeCardModel> arrangedCards = new();
    private readonly Dictionary<string, CollectionViewState> catalogViews = new();
    private CancellationTokenSource? catalogRequest;
    private List<ContinueCardModel> historyCardsData=new();
    private int historyColumns;
    private bool historyReflowScheduled;
    private long historyVersion=long.MinValue;
    private int historyLocale=-1;
    private sealed record CollectionViewState(List<AnimeCardModel> Items,List<AnimeCardModel> Visible,List<AnimeCardModel> Arranged,ObservableCollection<CatalogRow> Rows,int Columns,double Offset,string Query,string Genre,bool Sort,bool Paginated,SearchPage? Results,int NextPage,HashSet<int> Loaded);

    private readonly bool automaticCatalog;
    private readonly bool startupEnabled;
    private readonly StartupCatalogStore homeStore;
    private readonly Task<SearchPage?> cachedHome;
    private readonly Func<CancellationToken,Task<SearchPage>> homeFetch;
    private readonly CancellationTokenSource homeLifetime = new();
    private Task? homeStartupTask;
    private SearchPage? homeSnapshot;
    private bool windowClosed;
    internal int HomeCommitCount { get; private set; }
    internal Task StartHomeForDiagnostics() => EnsureHomeStartup();

    internal Func<string, int, Task<SearchPage>>? CatalogFetch;
    internal Func<bool>? HistoryConfirmation;
    private int nextCatalogPage;
    private readonly HashSet<int> loadedPages = new();
    private ScrollViewer? ExploreScroll => FindScroll(Catalog);
    private static ScrollViewer? FindScroll(DependencyObject parent) { if (parent is ScrollViewer scroll) return scroll; for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++) if (FindScroll(System.Windows.Media.VisualTreeHelper.GetChild(parent, i)) is { } found) return found; return null; }
    private bool connected, sortRating, paginated, cinema;
    public AnimeCardModel? Featured { get => featured; private set { if(ReferenceEquals(featured,value)) return; featured = value; PropertyChanged?.Invoke(this, new(nameof(Featured))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public MainWindow(bool connect = true, LibraryStore? listStore = null, HistoryStore? historyStore = null, StartupCatalogStore? startupStore = null, Func<CancellationToken,Task<SearchPage>>? startupFetch = null)
    {
        dataCache=new(api);
        automaticCatalog = connect;
        startupEnabled = connect || startupStore is not null;
        homeStore = startupStore ?? new StartupCatalogStore();
        homeFetch = startupFetch ?? FetchStartupHome;
        // Disk I/O and parsing overlap XAML creation; neither blocks the UI.
        cachedHome = startupEnabled ? homeStore.Read(homeLifetime.Token) : Task.FromResult<SearchPage?>(null);
        library = listStore ?? new LibraryStore(); history = historyStore ?? new HistoryStore();
        if(startupEnabled) {
            var recentId=history.Entries.FirstOrDefault()?.AnimeId;
            _=Task.Run(async () => {
                try {
                    var cached=await cachedHome.ConfigureAwait(false); if(cached is null) return;
                    var ranked=cached.Items.OrderByDescending(item=>item.Raw.Number("views")).ToList();
                    var hero=ranked.FirstOrDefault(item=>item.Id==recentId) ?? ranked.FirstOrDefault(item=>Data.WebUrl(item.Raw.Text("cover_uri"))) ?? ranked[0];
                    async Task Prime(string[] urls,int width,int height) {
                        foreach(var url in urls) if(await ArtworkCache.Shared.PrimeDisk(url,width,height,homeLifetime.Token).ConfigureAwait(false)) return;
                    }
                    var jobs=new List<Task> {Prime(new AnimeCardModel(hero).CoverFallbacks,1536,512)};
                    jobs.AddRange(ranked.Take(8).Select(item=>Prime(new AnimeCardModel(item).PosterFallbacks,256,256)));
                    await Task.WhenAll(jobs).ConfigureAwait(false);
                } catch(OperationCanceledException) { }
                catch(Exception error) when(error is System.IO.IOException or UnauthorizedAccessException) { }
            });
        }
        InitializeComponent(); DataContext = this; Catalog.ItemsSource=catalogRows;
        sidebarMotion = new SidebarMotion(Sidebar, NavItems, NavSelection);
        // The header is the first virtualized item, so it scrolls exactly as in V5.
        HistoryPage.Children.Remove(HistoryHeader); HistoryPage.RowDefinitions.Clear(); Grid.SetRow(HistoryCards,0);
        SizeChanged += (_, _) => ResizeLayout();
        SourceInitialized += (_, _) => WindowAppearance.DarkCaption(this);
        StateChanged += (_, _) => WindowFrame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        PreviewKeyDown += Window_PreviewKeyDown;
        Activated += (_, _) => { if (IsLoaded) RefreshHistory(); };
        HomePopularSkeleton.ItemsSource = Enumerable.Range(0,8);
        if (startupEnabled) Loaded += async (_, _) => { if(connect) catalogPump.Start(); await EnsureHomeStartup(); };
        catalogPump.Tick += async (_, _) => await PumpCatalog();
        Closed += (_, _) => { windowClosed=true; sidebarMotion.Dispose(); homeLifetime.Cancel(); operation?.Cancel(); catalogRequest?.Cancel(); api.Dispose(); Locale.Current.Changed -= Locale_Changed; historyPoll.Stop(); catalogPump.Stop(); };
        ResizeLayout(); RefreshHistory();
        historyPoll.Tick += (_, _) => { var stamp = System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(history.DirectoryPath, "history.json")); if (stamp != historyStamp) { historyStamp = stamp; RefreshHistory(); } }; historyPoll.Start();
        Locale.Current.Changed += Locale_Changed; LanguageSelector.SelectedIndex = L.Language == "ar" ? 1 : 0;
        Hero.SizeChanged += (_, _) => Hero.Clip = new System.Windows.Media.RectangleGeometry(new Rect(Hero.RenderSize), 12, 12);
        DetailsPage.SizeChanged += (_, _) => ResizeLayout();
        DetailHero.SizeChanged += (_, _) => DetailHero.Clip = new System.Windows.Media.RectangleGeometry(new Rect(DetailHero.RenderSize), 12, 12);
        DetailCopy.LayoutUpdated += (_, _) => {
            var height = Math.Min(4, Math.Floor(Math.Max(0, DetailCopy.RowDefinitions[2].ActualHeight) / 20)) * 20;
            if (Math.Abs(DetailSynopsis.MaxHeight - height) > 0.1) DetailSynopsis.MaxHeight = height;
        };
        HeroCopy.LayoutUpdated += (_, _) => {
            // Only show complete synopsis lines when long titles consume more of the banner.
            var available = Math.Max(0, HeroCopy.RowDefinitions[3].ActualHeight - HeroSynopsis.Margin.Top - HeroSynopsis.Margin.Bottom);
            var height = Math.Min(3, Math.Floor(available / 20)) * 20;
            if (Math.Abs(HeroSynopsis.MaxHeight - height) > 0.1) HeroSynopsis.MaxHeight = height;
        };
        // Usually ready by this point on warm launches, before Show/first paint.
        if(cachedHome.IsCompletedSuccessfully && cachedHome.Result is { } cached) ApplyHomeSnapshot(cached);
    }
    private void ResizeLayout()
    {
        if (Hero is null) return;
        ApplyResponsive(new Size(ActualWidth > 1 ? ActualWidth : Width, ActualHeight > 1 ? ActualHeight : Height));
    }
    internal void ApplyResponsive(Size size)
    {
        var width = size.Width;
        SidebarColumn.Width = new GridLength(width < 1450 ? 212 : 228);
        var content = Math.Max(600, width - SidebarColumn.Width.Value - 38);
        Hero.Height = Math.Clamp(content * 0.264, 290, 420);
        HeroCopy.Width = Math.Clamp(content * 0.49, 450, 650);
        HeroCopy.Height = Math.Min(340, Hero.Height - 48);
        HeroTitle.FontSize = width < 1450 ? 38 : width >= 1850 ? 52 : 46;
        HeroTitle.LineHeight = HeroTitle.FontSize * 1.15;
        HeroTitle.MaxHeight = Math.Ceiling(HeroTitle.LineHeight * 2) + 2;
        // The star-sized episode row owns all remaining space. Only the hero
        // adapts to short windows; no outer viewer or list height estimate.
        var detailViewport = PageHost.ActualHeight > 0 ? PageHost.ActualHeight : size.Height - 82;
        var compactDetails = detailViewport < 600;
        DetailHero.Height = Math.Clamp(detailViewport - 250, 270, 320);
        DetailContent.Margin = new Thickness(compactDetails ? 18 : 24);
        DetailTitle.FontSize = compactDetails ? 26 : 32;
        DetailTitle.LineHeight = DetailTitle.FontSize * 1.15;
        DetailTitle.MaxHeight = Math.Ceiling(DetailTitle.LineHeight * 2) + 2;
        DetailMetadata.Margin = compactDetails ? new Thickness(0,8,0,12) : new Thickness(0,12,0,16);
        DetailActions.Margin = new Thickness(0,compactDetails ? 10 : 18,0,0);
    }
    private async Task Run(string status, Func<CancellationToken, Task> action)
    {
        operation?.Cancel(); var source = new CancellationTokenSource(); operation = source;
        SetStatus(status); BusyBar.Visibility = Visibility.Visible;
        try { await action(source.Token); }
        catch (OperationCanceledException) { if (!source.IsCancellationRequested) SetStatus(L.T("Timeout")); }
        catch (Exception error)
        {
            if (!source.IsCancellationRequested)
            {
                SetStatus(L.Error(error));
                if (ServerOverlay.Visibility == Visibility.Visible) ServerStatus.Text = L.Error(error);
                else if (catalog.Count == 0) ShowEmpty(L.T("ConnectionFailed"), L.T("ConnectionHint"));
            }
        }
        finally { if (ReferenceEquals(operation, source)) { operation = null; BusyBar.Visibility = Visibility.Collapsed; } source.Dispose(); }
    }
    private Task EnsureHomeStartup() => homeStartupTask ??= StartHomeCore();
    private async Task<SearchPage> FetchStartupHome(CancellationToken ct)
    {
        await dataCache.Initialize(ct); connected=true;
        return await dataCache.Search("",0,ct);
    }
    private async Task StartHomeCore()
    {
        string? startupStatus=null;
        bool OwnStatus() => startupStatus is not null && L.KeyFor(StatusText.Text)==L.KeyFor(startupStatus);
        try {
            var cached=await cachedHome;
            if(windowClosed) return;
            if(cached is not null) ApplyHomeSnapshot(cached);
            if(page=="Home" && operation is null) { BusyBar.Visibility=Visibility.Visible; startupStatus=L.T(catalog.Count>0?"RefreshingHome":"Connecting"); SetStatus(startupStatus); }
            var fresh=await homeFetch(homeLifetime.Token);
            homeLifetime.Token.ThrowIfCancellationRequested();
            if(fresh.Items.Count==0) throw new UiException("EmptyTitles");
            ApplyHomeSnapshot(fresh);
            if(operation is null && (page=="Home" || OwnStatus())) SetStatus(L.T("HomeUpdated",fresh.Total));
            await homeStore.Save(fresh,homeLifetime.Token);
        }
        catch(OperationCanceledException) { }
        catch(Exception error) {
            if(!windowClosed && operation is null && (page=="Home" || OwnStatus())) {
                SetStatus(catalog.Count>0?L.T("CachedHomeOffline"):L.Error(error));
                if(catalog.Count==0 && page=="Home") ShowEmpty(L.T("ConnectionFailed"),L.T("ConnectionHint"));
            }
        }
        finally { if(!windowClosed && operation is null) BusyBar.Visibility=Visibility.Collapsed; }
    }
    private bool ApplyHomeSnapshot(SearchPage data)
    {
        if(windowClosed || StartupCatalogStore.Equivalent(homeSnapshot,data)) return false;
        var existing=catalog.ToDictionary(card=>card.Id);
        catalog=data.Items.DistinctBy(item=>item.Id).Select(item=>existing.TryGetValue(item.Id,out var card) && StartupCatalogStore.SameAnime(card.Source,item)?card:new AnimeCardModel(item,library.Contains(item.Id))).ToList();
        var next=catalog.ToDictionary(card=>card.Id);
        var historyChanged=history.Entries.Any(entry=>next.TryGetValue(entry.AnimeId,out var item) && (!existing.TryGetValue(entry.AnimeId,out var old) || old.Name!=item.Name || old.Source.Poster!=item.Source.Poster || old.CoverUrl!=item.CoverUrl));
        homeSnapshot=data; HomeCommitCount++;
        if(historyChanged) historyVersion=long.MinValue;
        PopulateHome();
        // A background Home refresh must not replace a user's search, filters,
        // scroll position, selected episode or active page.
        return true;
    }
    private void UpdateHomeLoadingState()
    {
        var ready=catalog.Count>0;
        HeroLive.Visibility=ready?Visibility.Visible:Visibility.Collapsed;
        HeroSkeleton.Visibility=ready?Visibility.Collapsed:Visibility.Visible;
        HomePopular.Visibility=ready?Visibility.Visible:Visibility.Collapsed;
        HomePopularSkeleton.Visibility=ready?Visibility.Collapsed:Visibility.Visible;
    }
    private async Task Connect()
    {
        if(startupEnabled && (homeStartupTask is null || !homeStartupTask.IsCompleted)) { await EnsureHomeStartup(); return; }
        await Run(L.T("Connecting"), async ct => {
            catalogRequest?.Cancel(); dataCache.Clear(); catalogViews.Clear(); connected=false;
            await dataCache.Initialize(ct); connected=true;
            var data=await dataCache.Search("",0,ct); ct.ThrowIfCancellationRequested();
            if(data.Items.Count==0) throw new UiException("EmptyTitles");
            ApplyHomeSnapshot(data);
            SetStatus(L.T("HomeUpdated",data.Total));
            await homeStore.Save(data,ct);
        });
    }
    private List<AnimeCardModel> Cards(IEnumerable<Anime> items) => items.DistinctBy(x => x.Id).Select(x => new AnimeCardModel(x, library.Contains(x.Id))).ToList();
    private void PopulateHome()
    {
        var previousFeatureId=Featured?.Id;
        var ranked = catalog.OrderByDescending(x => x.Popularity).ToList();
        var popular=ranked.Take(8).ToList();
        if(HomePopular.ItemsSource is not IEnumerable<AnimeCardModel> oldPopular || !oldPopular.SequenceEqual(popular)) HomePopular.ItemsSource=popular;
        features = ranked.Where(x => Data.WebUrl(x.Source.Raw.Text("cover_uri"))).Take(5).ToList();
        if (features.Count == 0) features = ranked.Take(5).ToList();
        var recent = history.Entries.Select(x => catalog.FirstOrDefault(c => c.Id == x.AnimeId)).FirstOrDefault(x => x is not null);
        if (recent is not null) { features.RemoveAll(x => x.Id == recent.Id); features.Insert(0, recent); features = features.Take(5).ToList(); }
        featureIndex=Math.Max(0,features.FindIndex(item=>item.Id==previousFeatureId));
        UpdateFeature(previousFeatureId is not null && features.Count>0 && features[featureIndex].Id!=previousFeatureId);
        RefreshHistory(); UpdateHomeLoadingState();
    }
    private void UpdateFeature(bool animate=true)
    {
        if (features.Count == 0) { Featured = null; HeroIndex.Text = ""; return; }
        var firstFeature=Featured is null;
        Featured = features[featureIndex]; HeroIndex.Text = $"{featureIndex + 1:00}  /  {features.Count:00}";
        var dotState=string.Join("\0",features.Select(item=>item.Id))+"|"+featureIndex;
        if(featureDotsState!=dotState) HeroDots.ItemsSource = Enumerable.Range(0, features.Count).Select(i => new { Index = i, DotWidth = i == featureIndex ? 20d : 8d, Fill = FindResource(i == featureIndex ? "AccentBrush" : "MutedBrush") }).ToList();
        featureDotsState=dotState;
        if(animate) Hero.BeginAnimation(OpacityProperty,new DoubleAnimation(0.65,1,TimeSpan.FromMilliseconds(230)));
        else if(firstFeature) { Hero.BeginAnimation(OpacityProperty,null); Hero.Opacity=1; }
    }
    private void RefreshHistory()
    {
        var file=new System.IO.FileInfo(System.IO.Path.Combine(history.DirectoryPath,"history.json"));
        var version=file.Exists?file.LastWriteTimeUtc.Ticks ^ file.Length:0;
        if(historyVersion==version && historyLocale==Locale.Current.Revision) { UpdateHistoryEmpty(history.Entries.Count); return; }
        historyVersion=version; historyLocale=Locale.Current.Revision;
        history.Reload();
        var cards = history.Entries.Select(entry =>
        {
            var anime = catalog.FirstOrDefault(x => x.Id == entry.AnimeId)?.Source
                ?? Anime.Parse(new System.Text.Json.Nodes.JsonObject { ["name"] = entry.AnimeName, ["poster_uri"] = entry.Poster, ["cover_uri"] = entry.Cover }, entry.AnimeId);
            return new ContinueCardModel(entry, anime);
        }).ToList();
        HomeHistory.ItemsSource = cards.DistinctBy(x => x.Entry.AnimeId).Take(5).ToList(); historyCardsData=cards.DistinctBy(x=>x.Entry.AnimeId).ToList(); ReflowHistory(true); ClearHistoryButton.IsEnabled = cards.Count > 0;
        UpdateHistoryEmpty(cards.Count);
    }
    private void HistoryCards_SizeChanged(object sender,SizeChangedEventArgs e)=>QueueHistoryReflow();
    private void HistoryCards_ScrollChanged(object sender,ScrollChangedEventArgs e) { if(e.ViewportWidthChange!=0) QueueHistoryReflow(); }
    private void QueueHistoryReflow()
    {
        if(historyReflowScheduled) return; historyReflowScheduled=true;
        _=Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=> { historyReflowScheduled=false; ReflowHistory(); }));
    }
    private void ReflowHistory(bool force=false)
    {
        if(HistoryCards is null) return;
        var scroll=FindScroll(HistoryCards); var width=scroll?.ViewportWidth>1?scroll.ViewportWidth:Math.Max(1,HistoryCards.ActualWidth);
        var columns=ResponsiveCardPanel.ColumnsFor(width,250,5); if(!force && columns==historyColumns) return;
        var offset=scroll?.VerticalOffset??0; historyColumns=columns;
        var chunks=historyCardsData.Chunk(columns).ToArray(); HistoryCards.ItemsSource=new object[] { HistoryHeader }.Concat(chunks.Select((items,i)=>(object)new ContinueRow(items,i==chunks.Length-1))).ToList();
        _=Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>scroll?.ScrollToVerticalOffset(offset)));
    }
    private void UpdateHistoryEmpty(int count)
    {
        HistoryEmpty.Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
        if(page=="History") { if(count==0) ShowEmpty(L.T("HistoryEmpty"),L.T("HistoryEmptyHint")); else EmptyState.Visibility=Visibility.Collapsed; }
    }
    private void CaptureCollection()
    {
        if(page is not ("Explore" or "Trending" or "Genres" or "Seasonal" or "My List") || collection.Count==0) return;
        catalogViews[page]=new(collection,visibleCatalog,arrangedCards,catalogRows,rowColumns,ExploreScroll?.VerticalOffset??0,query,selectedGenre,sortRating,paginated,results,nextCatalogPage,loadedPages.ToHashSet());
    }
    private bool RestoreCollection(string name)
    {
        if(page==name && collection.Count>0) return true;
        if(!catalogViews.TryGetValue(name,out var state)) return false;
        ShowPage(name);
        collection=state.Items; visibleCatalog=state.Visible; arrangedCards=state.Arranged; catalogRows=state.Rows; rowColumns=state.Columns; query=state.Query; selectedGenre=state.Genre; sortRating=state.Sort; paginated=state.Paginated; results=state.Results; nextCatalogPage=state.NextPage; loadedPages.Clear(); loadedPages.UnionWith(state.Loaded);
        Catalog.ItemsSource=catalogRows; UpdateCollectionHeading(name); UpdateGenreChips(name); UpdateCatalogFooter(); UpdateCollectionEmpty(); ReflowCatalog();
        var expected=catalogRows;
        _=Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=> { if(page==name && ReferenceEquals(catalogRows,expected)) ExploreScroll?.ScrollToVerticalOffset(state.Offset); }));
        return true;
    }
    private void ShowPage(string name)
    {
        if(page!=name) CaptureCollection();
        if(name!="Explore") catalogRequest?.Cancel();
        page = name; ServerOverlay.Visibility = Visibility.Collapsed; EmptyState.Visibility = Visibility.Collapsed;
        HomePage.Visibility = name == "Home" ? Visibility.Visible : Visibility.Collapsed;
        ExplorePage.Visibility = name is "Explore" or "Trending" or "My List" or "Genres" or "Seasonal" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = name == "History" ? Visibility.Visible : Visibility.Collapsed;
        DetailsPage.Visibility = name == "Details" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = name == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        // Details belong to the originating sidebar section; don't clear its indicator.
        var navigationPage = name == "Details" ? returnPage : name;
        foreach (var button in new[] { HomeNav, ExploreNav, TrendingNav, HistoryNav, ListNav, GenresNav, SeasonalNav, SettingsNav }) button.IsChecked = button.Tag?.ToString() == navigationPage;
        sidebarMotion.SyncSelection();
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(180)));
    }
    private void ShowEmpty(string title, string message) { EmptyTitle.Text = title; EmptyMessage.Text = message; EmptyState.Visibility = Visibility.Visible; }
    private void ShowCollection(string name, IEnumerable<AnimeCardModel> cards, bool canPage)
    {
        ShowPage(name); collection = cards.ToList(); paginated = canPage; selectedGenre = "";
        catalogRows=new(); arrangedCards=new(); rowColumns=0; Catalog.ItemsSource=catalogRows;
        UpdateCollectionHeading(name); UpdateGenreChips(name); FilterCollection(); ExploreScroll?.ScrollToTop();
    }
    private void UpdateCollectionHeading(string name)
    {
        PageTitle.Text = name switch { "Explore" => query.Length > 0 ? L.T("SearchResults") : L.T("ExploreAnime"), _ => L.T(name) };
        PageSubtitle.Text = name switch {
            "Trending" => L.T("TrendingHint"),
            "My List" => L.T("ListHint"),
            "Genres" => L.T("GenresHint"),
            "Seasonal" => L.T("SeasonalHint"),
            _ => query.Length > 0 ? L.T("SearchFor", query) : L.T("Discover") };
    }
    private void UpdateGenreChips(string name)
    {
        GenreChips.Children.Clear();
        foreach (var genre in collection.SelectMany(x => x.Genres).Distinct().Take(name == "Genres" ? 24 : 5))
        {
            var chip = new Button { Content = genre, Tag = genre, Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 7, 7), Padding = new Thickness(11, 7, 11, 7), FontSize = 11 };
            chip.Click += (_, _) => { selectedGenre = selectedGenre == genre ? "" : genre; FilterCollection(); }; GenreChips.Children.Add(chip);
            if(genre==selectedGenre) { chip.BorderBrush=(System.Windows.Media.Brush)FindResource("AccentBrush"); chip.Background=(System.Windows.Media.Brush)FindResource("RaisedBrush"); }
        }
    }
    private void FilterCollection()
    {
        IEnumerable<AnimeCardModel> filtered = collection;
        if (selectedGenre.Length > 0) filtered = filtered.Where(x => x.Genres.Contains(selectedGenre));
        if (sortRating) filtered = filtered.OrderByDescending(x => x.RatingValue);
        var cards = filtered.ToList(); visibleCatalog = cards; ReflowCatalog(true);
        foreach (var chip in GenreChips.Children.OfType<Button>()) { if (chip.Tag?.ToString() == selectedGenre) { chip.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush"); chip.Background = (System.Windows.Media.Brush)FindResource("RaisedBrush"); } else { chip.ClearValue(Button.BorderBrushProperty); chip.ClearValue(Button.BackgroundProperty); } }
        UpdateCatalogFooter();
        UpdateCollectionEmpty();
    }
    private void UpdateCollectionEmpty()
    {
        EmptyState.Visibility=Visibility.Collapsed;
        if(visibleCatalog.Count==0 && (!paginated || results is null || nextCatalogPage>=results.Pages)) ShowEmpty(page=="My List"?L.T("EmptyList"):L.T("EmptyMatches"),page=="My List"?L.T("EmptyListHint"):L.T("EmptyMatchesHint"));
    }
    private void UpdateCatalogFooter()
    {
        SortButton.Content = sortRating ? L.T("OriginalOrder") : L.T("SortRating");
        PageLabel.Text = L.T("CatalogCount", collection.Count);
        CatalogLoading.Visibility = paginated && loadingMore ? Visibility.Visible : Visibility.Collapsed;
        CatalogStatus.Text = paginated && loadingMore ? L.T("LoadingMore") : paginated && catalogFailures > 0 ? L.T("AutomaticRetry") : paginated && results is not null && nextCatalogPage >= results.Pages ? L.T("CatalogComplete") : "";
    }
    private async void Nav_Click(object sender, RoutedEventArgs e)
    {
        operation?.Cancel(); var target = ((RadioButton)sender).Tag?.ToString() ?? "Home";
        if(target is "Explore" or "Trending" or "Genres" or "Seasonal" or "My List" && RestoreCollection(target)) return;
        switch (target)
        {
            case "Home": ShowPage("Home"); RefreshHistory(); if (catalog.Count == 0 && !connected) await Connect(); break;
            case "History": ShowPage("History"); RefreshHistory(); break;
            case "Settings": ShowPage("Settings"); break;
            case "My List": ShowCollection(target, Cards(library.Items), false); break;
            case "Trending": ShowCollection(target, catalog.OrderByDescending(x => x.Popularity), false); break;
            case "Genres": ShowCollection(target, catalog, false); break;
            case "Seasonal":
                var year = catalog.Select(x => int.TryParse(x.Year, out var n) ? n : 0).DefaultIfEmpty(0).Max();
                ShowCollection(target, catalog.Where(x => x.Year == year.ToString()), false); break;
            default: if (collection.Count > 0 && paginated) ShowPage("Explore"); else { SearchBox.Clear(); await SearchCatalog("", 0); } break;
        }
    }
    private async Task SearchCatalog(string text, int number)
    {
        ShowPage("Explore"); catalogRequest?.Cancel(); catalogViews.Remove("Explore"); query = text; results = null; catalogFailures = 0; catalogRetryAt = default;
        await Run(L.T("FindingWatch"), async ct =>
        {
            if (!connected) { await dataCache.Initialize(ct); connected = true; }
            var data = await dataCache.Search(text, number, ct); ct.ThrowIfCancellationRequested(); results = data;
            loadedPages.Clear(); loadedPages.Add(number); nextCatalogPage = number + 1; ShowCollection("Explore", Cards(data.Items), true); SetStatus(L.T("FoundTitles", data.Total));
        });
    }
    private async Task OpenAnime(Anime anime, bool watch = false, string? episodeId = null)
    {
        returnPage = page == "Details" ? returnPage : page;
        currentAnime = anime; episodes = new(); EpisodeList.ItemsSource = null; EpisodeCount.Text = L.T("Loading"); EpisodeFilter.Clear();
        DetailHero.DataContext = new AnimeCardModel(anime, library.Contains(anime.Id)); ShowPage("Details"); FindScroll(EpisodeList)?.ScrollToTop();
        var loaded = false;
        await Run(L.T("LoadingDetails"), async ct =>
        {
            // Cached Home actions can be used while startup initialization is
            // still running. Share that work rather than issuing it again.
            await dataCache.Initialize(ct); connected=true;
            var detailTask=dataCache.Details(anime.Id,ct); var episodeTask=dataCache.Episodes(anime.Id,ct);
            await Task.WhenAll(detailTask,episodeTask); ct.ThrowIfCancellationRequested();
            var detail=await detailTask; currentAnime=detail; DetailHero.DataContext=new AnimeCardModel(detail,library.Contains(detail.Id)); episodes=await episodeTask;
            EpisodeList.ItemsSource = episodes; EpisodeCount.Text = L.T("EpisodeCount", episodes.Count); loaded = true;
            SetStatus(episodes.Count > 0 ? L.T("ChooseEpisode") : L.T("NoEpisodes"));
        });
        if (watch && loaded && page == "Details" && currentAnime?.Id == anime.Id && episodes.Count > 0)
            await OpenEpisode(episodes.FirstOrDefault(x => x.Id == episodeId) ?? episodes[0]);
    }
    private async Task OpenEpisode(Episode episode)
    {
        if (currentAnime is null) return;
        var anime = currentAnime; currentEpisode = episode;
        var index = episodes.FindIndex(x => x.Id == episode.Id); ServerPrevious.IsEnabled = index > 0; ServerNext.IsEnabled = index >= 0 && index + 1 < episodes.Count;
        ServerEpisode.Text = anime.Name + " · " + episode.Name; ServerList.ItemsSource = null; ServerStatus.Text = L.T("FindingQualities"); ServerOverlay.Visibility = Visibility.Visible;
        await Run(L.T("FindingStreams"), async ct =>
        {
            var servers = await api.Servers(anime.Id, episode.Id, ct); ct.ThrowIfCancellationRequested(); ServerList.ItemsSource = servers;
            ServerStatus.Text = servers.Count > 0 ? L.T("StreamReady") : L.T("NoStreams"); SetStatus(ServerStatus.Text);
        });
    }
    private async void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerList.SelectedItem is not VideoServer server || currentAnime is null || currentEpisode is null) return;
        ServerList.SelectedItem = null; var anime = currentAnime; var episode = currentEpisode;
        await Run(L.T("PreparingStream"), async ct =>
        {
            ServerStatus.Text = "Preparing " + server.Label + "…";
            var resolved = await api.Resolve(server, ct); ct.ThrowIfCancellationRequested(); history.Reload();
            var position = history.Position(anime.Id, episode.Id);
            var started = DateTime.UtcNow; await PlayerLauncher.Open(new(resolved, anime.Name + " · " + episode.Name, position, anime, episode, started));
            history.Save(anime, episode, position, history.Entries.FirstOrDefault(x => x.AnimeId == anime.Id && x.EpisodeId == episode.Id)?.Duration ?? 0, started);
            ServerOverlay.Visibility = Visibility.Collapsed; RefreshHistory(); SetStatus(L.T("NowPlaying"));
        });
    }
    private void ToggleSaved(Anime anime)
    {
        try { library.Toggle(anime); foreach (var item in catalog.Concat(collection).Concat(features).Concat(catalogViews.Values.SelectMany(view=>view.Items)).Distinct()) if (item.Id == anime.Id) item.IsSaved = library.Contains(anime.Id);
            if (DetailHero.DataContext is AnimeCardModel model && model.Id == anime.Id) model.IsSaved = library.Contains(anime.Id);
            catalogViews.Remove("My List");
            if (page == "My List") ShowCollection("My List", Cards(library.Items), false); SetStatus(library.Contains(anime.Id) ? L.T("AddedList") : L.T("RemovedList")); }
        catch (Exception) { SetStatus(L.T("ListSaveFailed")); }
    }
    private async void Card_Selected(object sender, RoutedEventArgs e) { if ((e.OriginalSource as AnimeCard ?? sender as AnimeCard)?.DataContext is AnimeCardModel card) await OpenAnime(card.Source); }
    private void Card_SaveRequested(object sender, RoutedEventArgs e) { if ((e.OriginalSource as AnimeCard ?? sender as AnimeCard)?.DataContext is AnimeCardModel card) ToggleSaved(card.Source); }
    private async void Continue_ResumeRequested(object sender, RoutedEventArgs e) { if ((e.OriginalSource as ContinueCard ?? sender as ContinueCard)?.DataContext is ContinueCardModel card) await OpenAnime(card.Source, true, card.Entry.EpisodeId); }
    private async void HeroWatch_Click(object sender, RoutedEventArgs e) { if (Featured is not null) await OpenAnime(Featured.Source, true); }
    private async void HeroInfo_Click(object sender, RoutedEventArgs e) { if (Featured is not null) await OpenAnime(Featured.Source); }
    private void HeroSave_Click(object sender, RoutedEventArgs e) { if (Featured is not null) ToggleSaved(Featured.Source); }
    private async void DetailWatch_Click(object sender, RoutedEventArgs e) { if (episodes.Count > 0) await OpenEpisode(episodes[0]); }
    private void DetailSave_Click(object sender, RoutedEventArgs e) { if (currentAnime is not null) ToggleSaved(currentAnime); }
    private void HeroPrevious_Click(object sender, RoutedEventArgs e) { if (features.Count > 0) { featureIndex = (featureIndex + features.Count - 1) % features.Count; UpdateFeature(); } }
    private void HeroNext_Click(object sender, RoutedEventArgs e) { if (features.Count > 0) { featureIndex = (featureIndex + 1) % features.Count; UpdateFeature(); } }
    private void HeroDot_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is int index && index < features.Count) { featureIndex = index; UpdateFeature(); } }
    private async void EpisodeList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (EpisodeList.SelectedItem is Episode episode) { EpisodeList.SelectedItem = null; await OpenEpisode(episode); } }
    private void EpisodeFilter_TextChanged(object sender, TextChangedEventArgs e) { if (EpisodeFilterHint is not null) EpisodeFilterHint.Visibility = string.IsNullOrEmpty(EpisodeFilter.Text) ? Visibility.Visible : Visibility.Collapsed; if (EpisodeList is not null) EpisodeList.ItemsSource = episodes.Where(x => (x.Name + " " + x.Id + " " + x.Subtitle).Contains(EpisodeFilter.Text, StringComparison.OrdinalIgnoreCase)).ToList(); }
    private async void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await SearchCatalog(SearchBox.Text.Trim(), 0); }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (SearchHint is not null) SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }
    private void FocusSearch_Click(object sender, RoutedEventArgs e) { SearchBox.Focus(); SearchBox.SelectAll(); }
    private async void ViewPopular_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); await SearchCatalog("", 0); }
    private async void ServerPrevious_Click(object sender, RoutedEventArgs e) { var index = episodes.FindIndex(x => x.Id == currentEpisode?.Id); if (index > 0) await OpenEpisode(episodes[index - 1]); }
    private async void ServerNext_Click(object sender, RoutedEventArgs e) { var index = episodes.FindIndex(x => x.Id == currentEpisode?.Id); if (index >= 0 && index + 1 < episodes.Count) await OpenEpisode(episodes[index + 1]); }
    private void ViewHistory_Click(object sender, RoutedEventArgs e) { ShowPage("History"); RefreshHistory(); }
    private async void Reconnect_Click(object sender, RoutedEventArgs e) => await Connect();
    private void Catalog_SizeChanged(object sender, SizeChangedEventArgs e) => QueueCatalogReflow();
    private void QueueCatalogReflow()
    {
        if(reflowScheduled) return; reflowScheduled=true;
        _=Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=> { reflowScheduled=false; ReflowCatalog(); }));
    }
    private void ReflowCatalog(bool force = false)
    {
        if (reflowing || Catalog is null) return;
        var scroll = ExploreScroll;
        var width = scroll?.ViewportWidth > 1 ? scroll.ViewportWidth : Math.Max(1, Catalog.ActualWidth);
        var columns = ResponsiveCardPanel.ColumnsFor(width);
        if (!force && columns == rowColumns) return;
        reflowing = true;
        try {
            var offset = scroll?.VerticalOffset ?? 0;
            var oldRowHeight = (scroll?.ExtentHeight ?? 0) / Math.Max(1, Catalog.Items.Count);
            var anchor = oldRowHeight > 0 ? offset / oldRowHeight * Math.Max(1, rowColumns) : 0;
            var rowColumnsBefore=rowColumns; rowColumns = columns;
            var sameColumns=columns==rowColumnsBefore;
            var append=sameColumns && visibleCatalog.Count>=arrangedCards.Count && arrangedCards.Select((card,i)=>ReferenceEquals(card,visibleCatalog[i])).All(match=>match);
            if(append && visibleCatalog.Count==arrangedCards.Count) return;
            if(append) {
                var start=arrangedCards.Count;
                if(catalogRows.Count>0 && catalogRows[^1].Count<columns) { start-=catalogRows[^1].Count; catalogRows.RemoveAt(catalogRows.Count-1); }
                if(catalogRows.Count>0) catalogRows[^1].SetLast(false);
                for(var i=start;i<visibleCatalog.Count;i+=columns) catalogRows.Add(new CatalogRow(visibleCatalog.Skip(i).Take(columns),i+columns>=visibleCatalog.Count));
            } else {
                catalogRows.Clear();
                for(var i=0;i<visibleCatalog.Count;i+=columns) catalogRows.Add(new CatalogRow(visibleCatalog.Skip(i).Take(columns),i+columns>=visibleCatalog.Count));
            }
            arrangedCards=visibleCatalog.ToList();
            if(!sameColumns) {
                Catalog.UpdateLayout();
                var target=force?offset:anchor/columns*((scroll?.ExtentHeight??0)/Math.Max(1,catalogRows.Count)); scroll?.ScrollToVerticalOffset(target);
            }
        } finally { reflowing = false; }
    }
    private async void Catalog_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0) QueueCatalogReflow();
        if ((automaticCatalog || CatalogFetch is not null) && (e.VerticalChange > 0 || automaticCatalog && e.ViewportHeightChange != 0)) await PumpCatalog();
    }
    internal async Task PumpCatalog()
    {
        if (page != "Explore" || !paginated || loadingMore || DateTime.UtcNow < catalogRetryAt || ExploreScroll is not { ViewportHeight: > 0 } scroll) return;
        if (scroll.ExtentHeight - scroll.VerticalOffset - scroll.ViewportHeight < Math.Max(450, scroll.ViewportHeight)) await LoadMore();
    }
    private async Task LoadMore()
    {
        if (loadingMore || !paginated || results is null || nextCatalogPage >= results.Pages || loadedPages.Contains(nextCatalogPage)) return;
        loadingMore = true; var requestedQuery = query; var requestedPage = nextCatalogPage; var generation = results; var targetCollection=collection;
        catalogRequest?.Dispose(); catalogRequest=new(); var token=catalogRequest.Token;
        UpdateCatalogFooter();
        try {
            var data = CatalogFetch is null ? await dataCache.Search(requestedQuery, requestedPage,token) : await CatalogFetch(requestedQuery, requestedPage);
            if (!ReferenceEquals(results, generation) || query != requestedQuery || !ReferenceEquals(collection,targetCollection) || page!="Explore") return;
            catalogFailures = 0; catalogRetryAt = default; loadedPages.Add(requestedPage); nextCatalogPage = requestedPage + 1;
            var ids = collection.Select(x => x.Id).ToHashSet(); collection.AddRange(Cards(data.Items.Where(x => ids.Add(x.Id))));
        } catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch (Exception error) {
            if (ReferenceEquals(results, generation) && ReferenceEquals(collection,targetCollection)) { catalogFailures++; catalogRetryAt = DateTime.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, Math.Min(5, catalogFailures)))); SetStatus(L.Error(error)); }
        }
        finally { loadingMore = false; if (page=="Explore" && ReferenceEquals(collection,targetCollection)) FilterCollection(); }
    }
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageSelector.SelectedItem is ComboBoxItem item && item.Tag is string language && language != L.Language)
            try { Locale.Current.SetLanguage(language); SetStatus(L.T("LanguageSaved")); } catch { SetStatus(L.T("LanguageSaveFailed")); }
    }
    private void Locale_Changed(object? sender, EventArgs e)
    {
        LanguageSelector.SelectedIndex = L.Language == "ar" ? 1 : 0;
        foreach (var card in catalog.Concat(collection).Concat(features).Concat(catalogViews.Values.SelectMany(view=>view.Items)).Distinct()) card.RefreshLanguage();
        if (DetailHero.DataContext is AnimeCardModel model) model.RefreshLanguage();
        RefreshHistory(); if (ExplorePage.Visibility == Visibility.Visible) { PageTitle.Text = L.T(page == "Explore" ? query.Length > 0 ? "SearchResults" : "ExploreAnime" : page); FilterCollection(); }
        PageSubtitle.Text = page switch { "Trending" => L.T("TrendingHint"), "My List" => L.T("ListHint"), "Genres" => L.T("GenresHint"), "Seasonal" => L.T("SeasonalHint"), _ => query.Length > 0 ? L.T("SearchFor", query) : L.T("Discover") };
        ServerStatus.Text = L.T("StreamHint");
        EpisodeCount.Text = L.T("EpisodeCount", episodes.Count); SetStatus(L.T("LanguageSaved"));
    }
    private void Continue_RemoveRequested(object sender, RoutedEventArgs e)
    {
        if ((e.OriginalSource as ContinueCard ?? sender as ContinueCard)?.DataContext is not ContinueCardModel card) return;
        try { history.RemoveAnime(card.Entry.AnimeId); RefreshHistory(); SetStatus(L.T("HistoryRemoved")); } catch { SetStatus(L.T("HistoryDeleteFailed")); }
    }
    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!(HistoryConfirmation?.Invoke() ?? ConfirmHistory())) return;
        try { history.Clear(); RefreshHistory(); SetStatus(L.T("HistoryCleared")); } catch { SetStatus(L.T("HistoryDeleteFailed")); }
    }
    private bool ConfirmHistory()
    {
        var dialog = new Window { Owner = this, Title = L.T("ClearHistoryTitle"), Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"), FlowDirection = Locale.Current.Direction };
        var content = new StackPanel { Margin = new Thickness(24) }; content.Children.Add(new TextBlock { Text = L.T("ClearHistoryQuestion"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,24) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; var cancel = new Button { Content = L.T("Cancel"), IsCancel = true, Margin = new Thickness(0,0,12,0) }; var clear = new Button { Content = L.T("ConfirmClear"), IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false; clear.Click += (_, _) => dialog.DialogResult = true; buttons.Children.Add(cancel); buttons.Children.Add(clear); content.Children.Add(buttons); dialog.Content = content; dialog.SourceInitialized += (_, _) => WindowAppearance.DarkCaption(dialog); return dialog.ShowDialog() == true;
    }
    private void ClearFilters_Click(object sender, RoutedEventArgs e) { selectedGenre = ""; sortRating = false; FilterCollection(); }
    private void Sort_Click(object sender, RoutedEventArgs e) { sortRating = !sortRating; FilterCollection(); }
    private void CloseServers_Click(object sender, RoutedEventArgs e) { operation?.Cancel(); ServerOverlay.Visibility = Visibility.Collapsed; }
    private void Back_Click(object sender, RoutedEventArgs e) { operation?.Cancel(); ShowPage(returnPage); }
    private void Cinema_Click(object sender, RoutedEventArgs e) { cinema = !cinema; Sidebar.Opacity = cinema ? 0.45 : 1; SetStatus(cinema ? L.T("CinemaOn") : L.T("CinemaOff")); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        if (e.Key == Key.Escape) { if (ServerOverlay.Visibility == Visibility.Visible) CloseServers_Click(this, new()); else if (page == "Details") Back_Click(this, new()); }
    }
    private void SetStatus(string text) => StatusText.Text = text;
    // Real captured catalog input for hidden layout diagnostics only; shipping startup always uses the API.
    internal void LoadDesignCatalog(IEnumerable<Anime> data) { catalogViews.Clear(); historyVersion=long.MinValue; catalog = Cards(data); PopulateHome(); ShowPage("Home"); SetStatus($"{catalog.Count} real catalog titles · Layout preview"); }
    internal void SetDesignPaging(IEnumerable<Anime> data, int pages = 3) { query = ""; catalogFailures = 0; catalogRetryAt = default; results = new(data.ToList(), 0, pages, pages * 30); loadedPages.Clear(); loadedPages.Add(0); nextCatalogPage = 1; ShowCollection("Explore", Cards(data), true); }
    internal Task LoadNextForDiagnostics() => LoadMore();
    internal void ShowDesignExplore() { if(!RestoreCollection("Explore")) ShowCollection("Explore",catalog,false); }
    internal void ShowDesignDetails(IEnumerable<Episode>? capturedEpisodes = null) { returnPage = page; if (catalog.FirstOrDefault() is { } item) { currentAnime = item.Source; DetailHero.DataContext = item; episodes = capturedEpisodes?.ToList() ?? new(); EpisodeList.ItemsSource = episodes; EpisodeCount.Text = L.T("EpisodeCount", episodes.Count); ShowPage("Details"); } }
    internal void ShowDesignServers(IEnumerable<VideoServer> servers) { ServerEpisode.Text = currentAnime?.Name + " · " + episodes.FirstOrDefault()?.Name; ServerList.ItemsSource = servers; ServerOverlay.Visibility = Visibility.Visible; ServerPrevious.IsEnabled = false; ServerNext.IsEnabled = episodes.Count > 1; }
}
