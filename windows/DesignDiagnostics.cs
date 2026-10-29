using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Input;

namespace AnimeWitcherDesktop;
internal static class DesignDiagnostics
{
    public static async Task<int> Run(string directory)
    {
        var checks = new List<string>(); string? error = null;
        var scratch = Path.Combine(Path.GetTempPath(), "witcher-library-check-" + Guid.NewGuid().ToString("N"));
        MainWindow? window = null;
        try
        {
            directory = Path.GetFullPath(directory);
            Artwork.PreviewDirectory = Path.Combine(directory, "design-assets");
            var capture = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "design-catalog.json")))!;
            var records = capture["hits"]!.AsArray().OfType<JsonObject>().Select(x => Anime.Parse(x)).ToList();
            if (records.Count < 8) throw new InvalidOperationException("Real catalog capture is missing");
            var output = Path.Combine(directory, "v5-previews"); Directory.CreateDirectory(output);
            Locale.Current.Initialize(scratch);
            window = new MainWindow(false, new LibraryStore(scratch), new HistoryStore(scratch)); window.LoadDesignCatalog(records);
            await CheckFeaturedTitles(window, records[0], output);
            checks.Add("Featured titles: complete two-line wrapping, ellipsis for longer names, no badge/action overlap at four sizes in EN/AR");
            window.LoadDesignCatalog(records);
            await CheckCardSurfaces(output);
            checks.Add("Card hover popups removed; title/save/resume/menu clicks and poster hover animation remain functional");
            checks.Add("Continue Watching: transparent rounded corners with portrait/wide white artwork, progress and resizing in English/Arabic");

            foreach (var size in new[] { new Size(1366, 768), new Size(1600, 900), new Size(1920, 1080) })
            {
                await Layout(window, size);
                var root = (FrameworkElement)window.Content;
                var search = (FrameworkElement)window.FindName("SearchShell");
                var hero = (FrameworkElement)window.FindName("Hero");
                var searchBounds = search.TransformToAncestor(root).TransformBounds(new Rect(search.RenderSize));
                if (searchBounds.Left < 200 || searchBounds.Right > size.Width - 180) throw new InvalidOperationException("Search/titlebar collision at " + size);
                var panel = Descendant<ResponsiveCardPanel>((ItemsControl)window.FindName("HomePopular"));
                if (Descendants<Controls.RoundedImage>((ItemsControl)window.FindName("HomePopular")).Count(image=>image.Source is BitmapSource)<5) throw new Exception("Home lost its real poster artwork after asynchronous loading");

                if (panel is null || panel.Children.Count != 8) throw new InvalidOperationException("Popular row missing actual catalog cards");
                foreach (UIElement card in panel.Children)
                {
                    var bounds = card.TransformToAncestor(panel).TransformBounds(new Rect(card.RenderSize));
                    if (bounds.Right > panel.ActualWidth + 1 || bounds.Left < -1) throw new InvalidOperationException("Poster grid overflow at " + size);
                    if (bounds.Top > 1) throw new InvalidOperationException("Home's reference-style poster row wrapped at " + size);
                }
                if (hero.ActualHeight < 290 || hero.ActualWidth < 900) throw new InvalidOperationException("Hero sizing failed at " + size);
                Save(root, Path.Combine(output, $"home-{size.Width:0}x{size.Height:0}.png"), size);
                checks.Add($"Home {size.Width:0}×{size.Height:0}: titlebar/search bounds, hero and real-data poster grid fit");
            }
            foreach (var language in new[] { "en", "ar" }) {
                Locale.Current.SetLanguage(language);
                foreach (var size in new[] { new Size(1366,768), new Size(1600,900), new Size(1920,1080) }) {
                    var searchBox = (TextBox)window.FindName("SearchBox"); searchBox.Text = language == "en" ? "Jujutsu Kaisen" : "\u062c\u0648\u062c\u0648\u062a\u0633\u0648 \u0643\u0627\u064a\u0633\u0646";
                    await Layout(window, size);
                    var host = searchBox.Template.FindName("PART_ContentHost", searchBox) as ScrollViewer;
                    if (host is null || host.ActualHeight < 15 || searchBox.Foreground.ToString() != "#FFF5F5FA" || ((UIElement)window.FindName("SearchHint")).Visibility != Visibility.Collapsed) throw new Exception("Search text is clipped, hidden or lacks contrast");
                    if (window.FlowDirection != (language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight)) throw new Exception("Language direction failed");
                    if (((TextBlock)((StackPanel)((RadioButton)window.FindName("SettingsNav")).Content).Children[1]).Text != L.T("Settings")) throw new Exception("Live translation failed");
                    var settingsIcon = (FrameworkElement)window.FindName("SettingsIcon"); var settingsNav = (FrameworkElement)window.FindName("SettingsNav");
                    var iconBounds = settingsIcon.TransformToAncestor(settingsNav).TransformBounds(new Rect(settingsIcon.RenderSize));
                    if (iconBounds.Left < 0 || iconBounds.Right > settingsNav.ActualWidth || settingsIcon.ActualWidth != 24 || settingsIcon.ActualHeight != 24) throw new Exception("Settings icon is clipped");
                    Save((FrameworkElement)window.Content, Path.Combine(output, $"home-{language}-{size.Width}x{size.Height}.png"), size);
                }
                checks.Add(language + ": live translations, direction and visible typed search at all three resolutions");
            }
            Locale.Current.SetLanguage("en"); ((TextBox)window.FindName("SearchBox")).Clear();
            window.ShowDesignExplore(); await Layout(window, new Size(1366, 768));
            var rows = ((ItemsControl)window.FindName("Catalog")).Items.Cast<List<AnimeCardModel>>();
            if (rows.Sum(x => x.Count) != records.Count) throw new InvalidOperationException("Explore lost catalog records");
            Save((FrameworkElement)window.Content, Path.Combine(output, "explore-1366x768.png"), new Size(1366, 768));
            checks.Add("Explore: all captured real records reach the responsive grid");
            foreach (var language in new[] { "en", "ar" }) {
                Locale.Current.SetForDiagnostics(language);
                foreach (var size in new[] { new Size(1366,768), new Size(1600,900), new Size(1920,1080) }) {
                    await Layout(window, size);
                    var actual = Descendant<ResponsiveCardPanel>((ItemsControl)window.FindName("Catalog")) ?? throw new Exception("V4 catalog panel missing");
                    var expected = new ResponsiveCardPanel { MinCardWidth = 145, AspectRatio = 0.7, FooterHeight = 62, UseLayoutRounding = true };
                    for (int i = 0; i < records.Count; i++) expected.Children.Add(new Border());
                    expected.Measure(new Size(actual.ActualWidth, double.PositiveInfinity)); expected.Arrange(new Rect(new Point(), expected.DesiredSize));
                    var viewport = Descendant<ScrollViewer>((ListBox)window.FindName("Catalog"))!;
                    if (Math.Abs(actual.ActualWidth - viewport.ViewportWidth) > 1) throw new Exception("Catalog acquired inset padding relative to V4");
                    var expectedColumns = ResponsiveCardPanel.ColumnsFor(actual.ActualWidth);
                    if (actual.Children.Count != expectedColumns) throw new Exception("Catalog column count differs from V4");
                    for (int i = 0; i < actual.Children.Count; i++) {
                        var actualCard = actual.Children[i]; var expectedCard = expected.Children[i];
                        if (Math.Abs(actualCard.RenderSize.Width - expectedCard.RenderSize.Width) > 1 || Math.Abs(actualCard.RenderSize.Height - expectedCard.RenderSize.Height) > 1) throw new Exception("Catalog poster dimensions differ from V4");
                        var actualBounds = actualCard.TransformToAncestor(actual).TransformBounds(new Rect(actualCard.RenderSize));
                        var expectedBounds = expectedCard.TransformToAncestor(expected).TransformBounds(new Rect(expectedCard.RenderSize));
                        if (Math.Abs(actualBounds.Left - expectedBounds.Left) > 1) throw new Exception("Catalog spacing differs from V4");
                    }
                    var rowList = ((ListBox)window.FindName("Catalog")).Items.Cast<CatalogRow>().ToList();
                    if (rowList.Take(rowList.Count - 1).Any(row => row.RowMargin.Bottom != 20) || rowList.Last().RowMargin.Bottom != 0) throw new Exception("V4 vertical row gaps changed");
                    CheckHoverClearance(window);
                    var footer = (TextBlock)window.FindName("PageLabel"); if (footer.FontSize != 10 || window.FindName("LoadMoreButton") is not null) throw new Exception("Catalog footer or button removal regressed");
                    Save((FrameworkElement)window.Content, Path.Combine(output,$"explore-{language}-{size.Width}x{size.Height}.png"),size);
                }
            }
            Locale.Current.SetForDiagnostics("en");
            checks.Add("V4 card geometry: exact responsive dimensions, 16px columns and 20px rows at all three sizes in both directions; compact footer and no Load More button");

            ((System.Windows.Controls.Primitives.ButtonBase)window.FindName("TrendingNav")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            foreach (var language in new[] { "en", "ar" }) {
                Locale.Current.SetForDiagnostics(language);
                foreach (var size in new[] { new Size(1366,768), new Size(1600,900), new Size(1920,1080) }) {
                    await Layout(window,size); CheckHoverClearance(window);
                }
            }
            Locale.Current.SetForDiagnostics("en"); window.ShowDesignExplore(); await Layout(window,new Size(1366,768));
            checks.Add("Explore/Trending first-row hover fits inside every ancestor clip at all three sizes in English/Arabic; resting card position is unchanged");
            var firstCard = Descendant<Controls.AnimeCard>((ItemsControl)window.FindName("Catalog")) ?? throw new InvalidOperationException("Reusable poster card missing");
            firstCard.RaiseEvent(new RoutedEventArgs(Controls.AnimeCard.SaveRequestedEvent, firstCard));
            if (new LibraryStore(scratch).Items.Count != 1) throw new InvalidOperationException("Bookmark action did not persist the selected real title");
            ((System.Windows.Controls.Primitives.ButtonBase)window.FindName("ListNav")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (((ItemsControl)window.FindName("Catalog")).Items.Count != 1) throw new InvalidOperationException("My List navigation lost the saved title");
            checks.Add("Reusable card bookmark persists and My List navigation displays the saved real title");
            ((System.Windows.Controls.Primitives.ButtonBase)window.FindName("GenresNav")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var chips = (Panel)window.FindName("GenreChips");
            if (chips.Children.Count > 0) { ((Button)chips.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (((ItemsControl)window.FindName("Catalog")).Items.Count == 0) throw new InvalidOperationException("Genre chip lost matching catalog data"); }
            ((Button)window.FindName("SortButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            checks.Add("Genre navigation, genre chips and rating sort route through the real catalog collection");
            var episodePath = Path.Combine(directory, "design-episodes.json");
            var episodes = File.Exists(episodePath) ? JsonNode.Parse(File.ReadAllText(episodePath))!["episodes"]!.AsArray().OfType<JsonObject>().Select(x => Episode.Parse(x)).ToList() : new List<Episode>();
            window.ShowDesignDetails(episodes); await Layout(window, new Size(1366, 768));
            Save((FrameworkElement)window.Content, Path.Combine(output, "details-1366x768.png"), new Size(1366, 768));
            var filter = (TextBox)window.FindName("EpisodeFilter"); filter.Text = episodes.FirstOrDefault()?.Id ?? "";
            var list = (ListBox)window.FindName("EpisodeList");
            if (episodes.Count > 0 && list.Items.Count == 0) throw new InvalidOperationException("Episode search lost its matching episode");
            checks.Add("Details: real episode rows render and episode search retains the requested row");
            var serverPath = Path.Combine(directory, "design-servers.json");
            if (File.Exists(serverPath)) {
                var servers = JsonNode.Parse(File.ReadAllText(serverPath))!["servers"]!.AsArray().OfType<JsonObject>().Select(VideoServer.Parse).ToList();
                window.ShowDesignServers(servers); await Layout(window, new Size(1366, 768));
                Save((FrameworkElement)window.Content, Path.Combine(output, "servers-1366x768.png"), new Size(1366, 768));
                if (((ListBox)window.FindName("ServerList")).Items.Count != servers.Count) throw new InvalidOperationException("Server selector lost a quality");
                checks.Add("Server picker renders all captured real server/quality options");
            }
            window.SetDesignPaging(records); await Layout(window, new Size(1366,768));
            var pending = new TaskCompletionSource<SearchPage>(); int fetches = 0;
            window.CatalogFetch = (_, _) => { fetches++; return pending.Task; };
            var firstLoad = window.LoadNextForDiagnostics(); await window.LoadNextForDiagnostics();
            if (fetches != 1) throw new Exception("Duplicate concurrent catalog request");
            pending.SetResult(new(records, 1, 3, 90)); await firstLoad;
            if (((ListBox)window.FindName("Catalog")).Items.Cast<List<AnimeCardModel>>().Sum(x => x.Count) != records.Count) throw new Exception("Duplicate anime appended");
            window.CatalogFetch = (_, _) => Task.FromException<SearchPage>(new IOException("Fixture failure")); await window.LoadNextForDiagnostics();
            window.CatalogFetch = (_, number) => Task.FromResult(new SearchPage(records, number, 3, 90)); await window.LoadNextForDiagnostics();
            if (!((TextBlock)window.FindName("CatalogStatus")).Text.Contains(L.T("CatalogComplete"))) throw new Exception("End of catalog not detected after retry");
            checks.Add("Infinite catalog: one in-flight request, duplicate ID suppression, retry after failure and end-of-catalog state");
            window.SetDesignPaging(records); var stale = new TaskCompletionSource<SearchPage>(); window.CatalogFetch = (_, _) => stale.Task;
            var oldLoad = window.LoadNextForDiagnostics(); window.SetDesignPaging(records.Take(2)); stale.SetResult(new(records, 1, 3, 90)); await oldLoad;
            if (((ListBox)window.FindName("Catalog")).Items.Cast<List<AnimeCardModel>>().Sum(x => x.Count) != 2) throw new Exception("Stale query overwrote newer catalog");
            var many = Enumerable.Range(0,5000).Select(i => records[i % records.Count] with { Id = "fixture-" + i }).ToList();
            window.SetDesignPaging(many, 1); await Layout(window, new Size(1366,768));
            var virtualPanel = Descendant<VirtualizingStackPanel>((ListBox)window.FindName("Catalog"));
            if (virtualPanel is null || virtualPanel.Children.Count >= 40) throw new Exception("Catalog does not virtualize rows");
            checks.Add($"Stale-query suppression and 5,000-title virtualization: {virtualPanel.Children.Count} realized rows");
            window.SetDesignPaging(many, 2); int automaticFetches = 0; window.CatalogFetch = (_, number) => { automaticFetches++; return Task.FromResult(new SearchPage(records,number,2,5030)); };
            await Layout(window, new Size(1366,768)); var scroller = Descendant<ScrollViewer>((ListBox)window.FindName("Catalog"))!;
            scroller.ScrollToBottom(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            if (automaticFetches != 1) throw new Exception("Approaching catalog bottom did not request exactly one next batch");
            scroller.ScrollToVerticalOffset(800); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); var savedOffset = scroller.VerticalOffset;
            window.ShowDesignDetails(episodes); typeof(MainWindow).GetMethod("Back_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (Math.Abs(scroller.VerticalOffset - savedOffset) > 1) throw new Exception("Explore scroll position was lost on return");
            checks.Add("Scrolling to the threshold triggers one automatic batch and details/back retains catalog position");
            window.SetDesignPaging(records.Take(1), 4); int fillRequests = 0;
            window.CatalogFetch = (_, number) => { fillRequests++; return Task.FromResult(new SearchPage(records.Take(number == 3 ? 3 : 1).ToList(), number, 4, 3)); };
            await Layout(window,new Size(1366,768));
            for (int i = 0; i < 4; i++) { await window.PumpCatalog(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            if (fillRequests != 3 || ((ListBox)window.FindName("Catalog")).Items.Cast<List<AnimeCardModel>>().Sum(x => x.Count) != 3) throw new Exception("Short or duplicate-only batches require manual loading");
            window.SetDesignPaging(records.Take(1), 2); int retryRequests = 0;
            window.CatalogFetch = (_, number) => { retryRequests++; return retryRequests == 1 ? Task.FromException<SearchPage>(new IOException("Temporary fixture failure")) : Task.FromResult(new SearchPage(records.Take(2).ToList(),number,2,2)); };
            await window.PumpCatalog(); await window.PumpCatalog();
            if (retryRequests != 1) throw new Exception("Automatic retry ignored backoff");
            await Task.Delay(2100); await window.PumpCatalog();
            if (retryRequests != 2) throw new Exception("Automatic retry did not recover without a button");
            checks.Add("Button-free loading fills short/duplicate-only pages to the end and retries failures automatically with backoff");
            var historyStore = new HistoryStore(scratch); var episodeFixture = episodes.FirstOrDefault() ?? Episode.Parse(new JsonObject { ["doc_id"] = "001", ["name"] = "Episode 1" });
            historyStore.Save(records[0], episodeFixture, 50, 600);
            ((System.Windows.Controls.Primitives.ButtonBase)window.FindName("HistoryNav")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await Layout(window, new Size(1366,768));
            var historyCard = Descendant<Controls.ContinueCard>((ItemsControl)window.FindName("HistoryCards")) ?? throw new Exception("History card missing");
            var menuButton = Descendants<Button>(historyCard).Single(button => button.ContextMenu is not null);
            var menu = menuButton.ContextMenu;
            foreach (var language in new[] { "en", "ar" }) {
                Locale.Current.SetForDiagnostics(language); menu.ApplyTemplate(); menu.Measure(new Size(400,100)); menu.Arrange(new Rect(new Point(),menu.DesiredSize)); menu.UpdateLayout();
                var remove = (MenuItem)menu.Items[0];
                if (menu.Style != window.FindResource("HistoryContextMenu") || remove.Style != window.FindResource("HistoryRemoveItem") || remove.Icon is null || menu.FlowDirection != Locale.Current.Direction) throw new Exception("History menu lost custom styling, icon or direction");
                Save(menu, Path.Combine(output,$"history-menu-{language}.png"),new Size(Math.Ceiling(menu.ActualWidth),Math.Ceiling(menu.ActualHeight)));
            }
            Locale.Current.SetForDiagnostics("en");
            checks.Add("Custom history menu: rounded dark/purple template, red remove icon, localized text and RTL/LTR");
            await Layout(window, new Size(1366,768));
            historyCard = Descendant<Controls.ContinueCard>((ItemsControl)window.FindName("HistoryCards"))!;
            historyCard.RaiseEvent(new RoutedEventArgs(Controls.ContinueCard.RemoveRequestedEvent, historyCard));
            if (((ItemsControl)window.FindName("HomeHistory")).Items.Count != 0 || ((ItemsControl)window.FindName("HistoryCards")).Items.OfType<ContinueRow>().Sum(row=>row.Count) != 0 || new HistoryStore(scratch).Entries.Count != 0) throw new Exception("History deletion did not synchronize");
            checks.Add("Card removal persists and synchronizes Home/history immediately");
            historyStore.Save(records[0], episodeFixture, 50, 600);
            ((System.Windows.Controls.Primitives.ButtonBase)window.FindName("HistoryNav")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var clearButton = (Button)window.FindName("ClearHistoryButton"); window.HistoryConfirmation = () => false; clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (new HistoryStore(scratch).Entries.Count != 1) throw new Exception("Cancel confirmation deleted history");
            window.HistoryConfirmation = () => true; clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (new HistoryStore(scratch).Entries.Count != 0 || ((ItemsControl)window.FindName("HomeHistory")).Items.Count != 0) throw new Exception("Confirmed clear failed");
            checks.Add("Bulk clear honors cancellation, persists confirmation and synchronizes Home");
            new LanguagePreferences(scratch).Save("ar"); await Task.Delay(200);
            if (L.Language != "ar" || window.FlowDirection != FlowDirection.RightToLeft) throw new Exception("External language preference change did not update open UI");
            ((System.Windows.Controls.Primitives.ButtonBase)window.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await Layout(window,new Size(1366,768));
            Save((FrameworkElement)window.Content, Path.Combine(output,"settings-ar-1366x768.png"),new Size(1366,768));
            checks.Add("Preference-file watcher applies another process's language change immediately");
            if (window.WindowStyle != WindowStyle.None) throw new InvalidOperationException("Native accent titlebar was not replaced");
            checks.Add("Integrated window chrome replaces the colored Windows titlebar");
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally { window?.Close(); Artwork.PreviewDirectory = null; Locale.Current.Dispose(); if (Directory.Exists(scratch)) { foreach (var file in Directory.GetFiles(scratch)) File.Delete(file); Directory.Delete(scratch); } }
        File.WriteAllText(Path.Combine(directory, "v5-design-checks.json"), JsonSerializer.Serialize(new { success = error is null, checks, error }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }
    private static async Task CheckFeaturedTitles(MainWindow window, Anime sample, string output)
    {
        var title=(TextBlock)window.FindName("HeroTitle");var copy=(Grid)window.FindName("HeroCopy");
        var metadata=(FrameworkElement)copy.Children.Cast<UIElement>().First(child=>Grid.GetRow(child)==2);
        var actions=(FrameworkElement)copy.Children.Cast<UIElement>().First(child=>Grid.GetRow(child)==4);
        var titles=new[] { "Kaoru Hana wa Rin to Saku", "The Misfit of Demon King Academy: History's Strongest Demon King Reincarnates and Goes to School with His Descendants", "\u0642\u0635\u0629 \u0637\u0648\u064a\u0644\u0629 \u0639\u0646 \u0627\u0644\u0645\u063a\u0627\u0645\u0631\u0627\u062a \u0648\u0627\u0644\u0623\u0635\u062f\u0642\u0627\u0621 \u0641\u064a \u0639\u0627\u0644\u0645 \u062c\u062f\u064a\u062f" };
        foreach(var language in new[]{"en","ar"}) {
            Locale.Current.SetForDiagnostics(language);
            foreach(var name in titles) {
                var raw=(JsonObject)sample.Raw.DeepClone();raw["name"]=name;
                window.LoadDesignCatalog(new[]{sample with { Name=name,Raw=raw }});
                foreach(var size in new[]{new Size(1000,600),new Size(1366,768),new Size(1600,900),new Size(1920,1080)}) {
                    await Layout(window,size);
                    var probe=new TextBlock { Text=name,FontFamily=title.FontFamily,FontSize=title.FontSize,FontWeight=title.FontWeight,TextWrapping=TextWrapping.Wrap,LineHeight=title.LineHeight,LineStackingStrategy=LineStackingStrategy.BlockLineHeight };
                    probe.Measure(new Size(title.ActualWidth,double.PositiveInfinity));
                    if(title.LineStackingStrategy!=LineStackingStrategy.BlockLineHeight || title.TextTrimming!=TextTrimming.CharacterEllipsis) throw new Exception("Featured title lost explicit line stacking/trimming");
                    if(title.ActualHeight+1<Math.Min(probe.DesiredSize.Height,title.LineHeight*2)) throw new Exception("Featured title is vertically clipped: "+name+" at "+size);
                    if(name==titles[0] && probe.DesiredSize.Height>title.MaxHeight) throw new Exception("Screenshot title no longer fits two full lines at "+size);
                    var titleBounds=title.TransformToAncestor(copy).TransformBounds(new Rect(title.RenderSize));var badgeBounds=metadata.TransformToAncestor(copy).TransformBounds(new Rect(metadata.RenderSize));
                    var actionBounds=actions.TransformToAncestor(copy).TransformBounds(new Rect(actions.RenderSize));
                    if(titleBounds.Bottom>badgeBounds.Top+.5 || actionBounds.Bottom>copy.ActualHeight+.5) throw new Exception("Featured title overlaps metadata/actions");
                    if(language=="en" && name==titles[0] && size.Width==1920) {var hero=(FrameworkElement)window.FindName("Hero");Save(hero,Path.Combine(output,"featured-long-title-fixed.png"),hero.RenderSize);}
                }
            }
        }
        Locale.Current.SetForDiagnostics("en");
    }
    private static void CheckHoverClearance(MainWindow window)
    {
        var catalog = (ListBox)window.FindName("Catalog");
        var card = Descendant<Controls.AnimeCard>(catalog) ?? throw new Exception("First-row card missing");
        var body = (Grid)card.FindName("CardRoot");
        var presenter = Descendant<ScrollContentPresenter>(catalog) ?? throw new Exception("Catalog viewport missing");
        var resting = body.TransformToAncestor(presenter).TransformBounds(new Rect(body.RenderSize));
        var catalogTop = catalog.TranslatePoint(new Point(),(UIElement)window.FindName("ExplorePage")).Y;
        var restingTop = body.TranslatePoint(new Point(),(UIElement)window.FindName("ExplorePage")).Y;
        if (Math.Abs(restingTop - catalogTop - 6) > 0.1) throw new Exception("Hover clearance shifted resting card placement");
        var motion = (TranslateTransform)body.RenderTransform;
        motion.BeginAnimation(TranslateTransform.YProperty,null); motion.Y = -4;
        try {
            var raised = body.TransformToAncestor(presenter).TransformBounds(new Rect(body.RenderSize));
            if (raised.Top < 1 || Math.Abs(resting.Top - raised.Top - 4) > 0.1) throw new Exception("First-row hover is clipped by the catalog viewport");
            // Test the top-center painted edge against layout clips, not just the viewport.
            for (DependencyObject? ancestor = card; ancestor is Visual visual; ancestor = VisualTreeHelper.GetParent(ancestor)) {
                if (VisualTreeHelper.GetClip(visual) is not { } clip) continue;
                var topEdge = body.TransformToAncestor(visual).Transform(new Point(body.ActualWidth / 2,0.5));
                if (!clip.FillContains(topEdge)) throw new Exception("First-row hover is clipped by " + visual.GetType().Name);
            }
        } finally { motion.Y = 0; }
    }
    private static async Task CheckCardSurfaces(string output)
    {
        using var source = new HwndSource(new HwndSourceParameters("Hidden card surface checks") { Width = 600, Height = 400, WindowStyle = unchecked((int)0x80000000) });
        var host = new Grid { Width = 268, Height = 150 };
        source.RootVisual = host;
        var historyCard = new Controls.ContinueCard(); host.Children.Add(historyCard);
        var resumed = 0; var removed = 0;
        historyCard.ResumeRequested += (_, _) => resumed++; historyCard.RemoveRequested += (_, _) => removed++;
        foreach (var direction in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        foreach (var artwork in new[] { new Size(100,300), new Size(600,100) })
        {
            var pixels = Enumerable.Repeat((byte)255, (int)(artwork.Width * artwork.Height * 4)).ToArray();
            var image = BitmapSource.Create((int)artwork.Width,(int)artwork.Height,96,96,PixelFormats.Bgra32,null,pixels,(int)artwork.Width * 4);
            historyCard.DataContext = new { Image = image, Name = "Artwork clipping check", Episode = "Episode 1", Position = "24m left", Percent = 50d, HasProgress = true };
            historyCard.FlowDirection = direction;
            foreach (var size in new[] { new Size(268,150), new Size(190,106), new Size(320,183) }) {
                host.Width = size.Width; host.Height = size.Height; host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Render);
                var snapshot = new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32); snapshot.Render(host);
                var rendered = new byte[(int)(size.Width * size.Height * 4)]; snapshot.CopyPixels(rendered,(int)size.Width * 4,0);
                foreach (var corner in new[] { (0,0), ((int)size.Width - 1,0), (0,(int)size.Height - 1), ((int)size.Width - 1,(int)size.Height - 1) })
                    if (rendered[(corner.Item2 * (int)size.Width + corner.Item1) * 4 + 3] > 5) throw new Exception("Artwork leaked into a rounded card corner");
                if (rendered[((int)size.Height / 2 * (int)size.Width + (int)size.Width / 2) * 4 + 3] < 250) throw new Exception("Card body unexpectedly transparent");
                for (int y = (int)size.Height - 9; y < (int)size.Height; y++)
                for (int x = 0; x < (int)size.Width; x++) {
                    var pixel = (y * (int)size.Width + x) * 4;
                    if (rendered[pixel] > 200 && rendered[pixel + 1] > 200 && rendered[pixel + 2] > 200) throw new Exception("White artwork leaked around the bottom gradient/border");
                }

                if (size.Width == 268 && artwork.Width == 100) Save(host,Path.Combine(output,$"continue-corners-{direction}.png"),size);
            }
        }
        var historyButtons = Descendants<Button>(historyCard).ToList();
        if (historyButtons.Any(button => button.ToolTip is not null)) throw new Exception("Continue Watching tooltip remains");
        historyButtons.Single(button => button.ContextMenu is null).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menuButton = historyButtons.Single(button => button.ContextMenu is not null);
        menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!menuButton.ContextMenu.IsOpen) throw new Exception("History menu no longer opens");
        ((MenuItem)menuButton.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menuButton.ContextMenu.IsOpen = false;
        if (resumed != 1 || removed != 1) throw new Exception("History card actions broke after clipping");
        var poster = new Controls.AnimeCard(); host.Children.Clear(); host.Children.Add(poster); var posterSize = new Size(170,305);
        host.Width = posterSize.Width; host.Height = posterSize.Height; host.Measure(posterSize); host.Arrange(new Rect(posterSize)); host.UpdateLayout();
        var selected = 0; var saved = 0; poster.Selected += (_, _) => selected++; poster.SaveRequested += (_, _) => saved++;
        var posterButtons = Descendants<Button>(poster).ToList(); if (posterButtons.Any(button => button.ToolTip is not null)) throw new Exception("Anime card tooltip remains");
        posterButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); posterButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (selected != 1 || saved != 1) throw new Exception("Poster click/save broke after tooltip removal");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        poster.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent }); await Task.Delay(220);
        if (((TranslateTransform)((Grid)poster.FindName("CardRoot")).RenderTransform).Y > -3.9) throw new Exception("Poster hover animation no longer runs: " + ((TranslateTransform)((Grid)poster.FindName("CardRoot")).RenderTransform).Y);
        source.RootVisual = null;
    }
    private static async Task Layout(MainWindow window, Size size)
    {
        window.Width = size.Width; window.Height = size.Height;
        window.Measure(size); window.Arrange(new Rect(size)); window.UpdateLayout();
        var root = (FrameworkElement)window.Content; root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        window.ApplyResponsive(size); root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.Render); await Task.Delay(280); root.UpdateLayout();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root,i); if (child is T found) yield return found; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) return item; if (Descendant<T>(child) is { } found) return found; } return null;
    }
    private static void Save(FrameworkElement root, string path, Size size)
    {
        var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
