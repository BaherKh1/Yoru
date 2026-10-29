using Orientation = Android.Widget.Orientation;
using OperationCanceledException = System.OperationCanceledException;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using AnimeWitcherDesktop;
using Yoru.Core;
using DownloadStatus=Yoru.Core.DownloadStatus;
namespace Yoru.AndroidApp;
[Activity(Label="YORU",MainLauncher=true,Exported=true,ConfigurationChanges=ConfigChanges.Orientation|ConfigChanges.ScreenSize|ConfigChanges.ScreenLayout|ConfigChanges.SmallestScreenSize)]
public sealed class MainActivity : LocalizedActivity
{
    private AppState State=>YoruApp.State;
    private Dialog? downloadDialog;
    private Dialog? watchDialog;private CancellationTokenSource watchLookup=new();private int watchRevision;private string? checkingEpisode;
    private readonly HashSet<string> expandedDownloads=new();
    private FrameLayout root=null!;private LinearLayout chrome=null!;private FloatingNavigation navigation=null!;private View navScrim=null!;private int safeTop,safeBottom,safeLeft,safeRight,keyboardBottom;private RecyclerView list=null!;private GridLayoutManager grid=null!;private FeedAdapter adapter=null!;private EditText search=null!;private TextView heading=null!;private ImageButton back=null!;
    private readonly Dictionary<string,int> positions=new();
    private readonly ViewportBookmarks viewports=new();private CatalogSession? returnCatalog;
    private PendingViewport? pendingViewport;private ViewportPreDraw? viewportPreDraw;private bool deferCatalogPrefetch;
    private sealed class PendingViewport(string context,ViewportAnchor anchor){public string Context{get;}=context;public ViewportAnchor Anchor{get;}=anchor;public int Attempts;}
    private string ViewportKey=>page+"\0"+(page=="Explore"?query:page=="Genres"?genre:page=="Details"?anime?.Id:"")+"\0"+(page=="Explore" && sort?"rating":"default");
    private int ViewOffset(View child)=>grid.GetDecoratedTop(child)-((child.LayoutParameters as RecyclerView.LayoutParams)?.TopMargin??0)-list.PaddingTop;
    private void SaveViewport(){
        if(adapter.ContextKey!=ViewportKey)return;
        var index=grid.FindFirstVisibleItemPosition();if(index<0 || index>=adapter.ItemCount || grid.FindViewByPosition(index) is not { } child)return;
        positions[page]=index;viewports.Save(ViewportKey,adapter.Row(index).Key,index,ViewOffset(child));
        if(Paged && catalog is not null)catalog.ScrollPosition=index;
    }
    private bool CommitViewportBeforeDraw(){
        var pending=pendingViewport;if(pending is null)return true;
        if(pending.Context!=ViewportKey){pendingViewport=null;return true;}
        if(adapter.ContextKey!=pending.Context)return false;
        if(adapter.ItemCount==0){pendingViewport=null;return true;}
        var index=pending.Anchor.Resolve(adapter.RowKeys);var child=grid.FindViewByPosition(index);
        if(child is not null && Math.Abs(ViewOffset(child)-pending.Anchor.OffsetPixels)<=1){pendingViewport=null;return true;}
        // Permit normal boundary clamping after an actual viewport size change.
        if(pending.Attempts++>=3){pendingViewport=null;return true;}
        grid.ScrollToPositionWithOffset(index,pending.Anchor.OffsetPixels);list.RequestLayout();return false;
    }
    private sealed class ViewportPreDraw(MainActivity owner):Java.Lang.Object,ViewTreeObserver.IOnPreDrawListener{public bool OnPreDraw()=>owner.CommitViewportBeforeDraw();}
    private string page="Home",returnPage="Home",query="",episodeQuery="",genre="";private bool settingSearch,sort,active;private Anime? anime;private IReadOnlyList<Episode> episodes=Array.Empty<Episode>();private CatalogSession? catalog;private CancellationTokenSource screen=new(),debounce=new();private int feedRevision,featureIndex,detailsRevision;private bool homeLaunchPending,scrollingTabTop;private string? tabTopPending;private bool moreTransitionPending;private bool detailsLoading;private string? detailsError;
    // Offline mode: the last confirmed reachability, where the user was before the
    // connection dropped, and the page we forced them onto so we can hand it back.
    private ConnectivityMonitor? connectivity;private bool offline,offlineNoticeShown;private string? offlineResumePage,offlineResumeReturn,offlineLanding,lastNotice;private Anime? offlineResumeAnime;private View? notice;private long lastNoticeAt;
    internal int CatalogWidthPixels=>list is {Width:>0}?list.Width-list.PaddingLeft-list.PaddingRight:ContentWidthPixels;
    public int Columns=>MobileLayout.ColumnsForPage(page,page=="Explore"?(int)(CatalogWidthPixels/Density):Resources!.Configuration!.ScreenWidthDp);
    internal bool CompactExplore=>page=="Explore";
    protected override void OnCreate(Bundle? savedInstanceState){
        base.OnCreate(savedInstanceState);
        connectivity=new ConnectivityMonitor(this);offline=!connectivity.Current;
        var savedPage=savedInstanceState?.GetString("page")??(Intent?.GetStringExtra("page")=="Downloads"?"Downloads":"Home");
        var savedReturn=savedInstanceState?.GetString("return")??"Home";
        page=MobileNavigation.NormalizePage(savedPage);returnPage=MobileNavigation.NormalizePage(savedReturn);query=savedInstanceState?.GetString("query")??"";
        anime=State.FindAnime(savedInstanceState?.GetString("anime")??"")??YoruApp.Downloads.Items.FirstOrDefault(item=>item.Anime.Id==savedInstanceState?.GetString("anime"))?.Anime;
        foreach(var id in savedInstanceState?.GetStringArrayList("expanded-downloads")??Array.Empty<string>())expandedDownloads.Add(id);
        if(page=="Details" && anime is null)page="Home";
        if(offline){
            offlineResumePage=savedInstanceState?.GetString("offline-resume");
            offlineResumeReturn=savedInstanceState?.GetString("offline-return");
            offlineResumeAnime=State.FindAnime(savedInstanceState?.GetString("offline-anime")??"");
            offlineLanding=savedInstanceState?.GetString("offline-landing");
            if(OfflineNavigation.OnlineOnly(page) && offlineResumePage is null){offlineResumePage=page;offlineResumeReturn=returnPage;offlineResumeAnime=anime;offlineLanding="Downloads";}
        }
        page=OfflineNavigation.StartPage(!offline,page);homeLaunchPending=page=="Home";
        BuildShell();OnBackPressedDispatcher.AddCallback(this,new BackAction(GoBack));
        if(!offline)_=Bootstrap();
        if(page=="Details" && anime is not null)_=LoadDetails(anime,false);else if(Paged) {catalog=State.Catalog(page=="Explore"?query:"");_=LoadMore();}
        _=Rebuild();
    }
    private bool Paged=>page is "Explore" or "Seasonal" or "Genres";
    internal int SafeTop=>safeTop;
    internal int HeroHeight=>MobileLayout.HeroHeight(Resources!.Configuration!.ScreenWidthDp,Resources.Configuration.ScreenHeightDp,safeTop/Density,Resources.Configuration.FontScale);
    internal int ContentWidthPixels=>Resources!.DisplayMetrics!.WidthPixels-safeLeft-safeRight;
    private float Density=>Resources!.DisplayMetrics!.Density;
    private void BuildShell(){
        AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(Window!,false);
        if(!OperatingSystem.IsAndroidVersionAtLeast(35)){Window!.SetStatusBarColor(Android.Graphics.Color.Transparent);Window.SetNavigationBarColor(Android.Graphics.Color.Transparent);}
        if(OperatingSystem.IsAndroidVersionAtLeast(29)){if(!OperatingSystem.IsAndroidVersionAtLeast(35))Window!.StatusBarContrastEnforced=false;Window!.NavigationBarContrastEnforced=false;}
        var insetsController=AndroidX.Core.View.WindowCompat.GetInsetsController(Window!,Window!.DecorView);
        if(insetsController is not null){insetsController.AppearanceLightStatusBars=false;insetsController.AppearanceLightNavigationBars=false;}
        Window.SetSoftInputMode(OperatingSystem.IsAndroidVersionAtLeast(30)?SoftInput.AdjustNothing:SoftInput.AdjustResize);
        root=new FrameLayout(this){Focusable=true,FocusableInTouchMode=true,DescendantFocusability=DescendantFocusability.BeforeDescendants};root.SetBackgroundColor(Ui.Background);root.LayoutDirection=L.Language=="ar"?Android.Views.LayoutDirection.Rtl:Android.Views.LayoutDirection.Ltr;SetContentView(root);
        list=new RecyclerView(this);list.SetClipToPadding(false);list.SetItemViewCacheSize(6);list.VerticalScrollBarEnabled=false;list.PreserveFocusAfterLayout=false;
        list.AddOnLayoutChangeListener(new CatalogWidthChanged(this));
        grid=new GridLayoutManager(this,Columns);adapter=new FeedAdapter(this);grid.SetSpanSizeLookup(new FeedAdapter.Spans(adapter,Columns));list.SetLayoutManager(grid);list.SetAdapter(adapter);list.SetItemAnimator(null);
        list.AddOnScrollListener(new MoreScroll(()=>{if(!deferCatalogPrefetch && pendingViewport is null && Paged && grid.FindLastVisibleItemPosition()>=adapter.ItemCount-8)_=LoadMore();},state=>{if(state==RecyclerView.ScrollStateDragging){homeLaunchPending=false;tabTopPending=null;scrollingTabTop=false;pendingViewport=null;deferCatalogPrefetch=false;}else if(state==RecyclerView.ScrollStateIdle)scrollingTabTop=false;}));
        root.AddView(list,new FrameLayout.LayoutParams(-1,-1));viewportPreDraw=new ViewportPreDraw(this);list.ViewTreeObserver!.AddOnPreDrawListener(viewportPreDraw);
        chrome=Ui.Vertical(this);chrome.Background=new Android.Graphics.Drawables.GradientDrawable(Android.Graphics.Drawables.GradientDrawable.Orientation.TopBottom,new[]{Android.Graphics.Color.ParseColor("#FF090C14").ToArgb(),Android.Graphics.Color.ParseColor("#F5090C14").ToArgb()});
        var top=new LinearLayout(this){Orientation=Orientation.Horizontal};top.SetGravity(GravityFlags.CenterVertical);top.SetPadding(D(18),0,D(18),0);
        back=StreamActions.Button(this,"Back",L.T("Back"),GoBack);if(L.Language=="ar")back.ScaleX=-1;top.AddView(back,new LinearLayout.LayoutParams(D(48),D(48)){MarginEnd=D(6)});
        var logo=new ImageView(this);logo.SetImageResource(Resource.Drawable.yoru_mark);logo.SetScaleType(ImageView.ScaleType.FitCenter);top.AddView(logo,new LinearLayout.LayoutParams(D(32),D(38)){MarginEnd=D(10)});
        heading=Ui.Label(this,"YORU",23);heading.SetTypeface(null,Android.Graphics.TypefaceStyle.Bold);top.AddView(heading,new LinearLayout.LayoutParams(0,-1,1));chrome.AddView(top,new LinearLayout.LayoutParams(-1,D(62)));
        search=Ui.Search(this,L.T("SearchPlaceholder"));search.TextChanged+=(_,_)=>SearchChanged();chrome.AddView(search,new LinearLayout.LayoutParams(-1,D(46)){LeftMargin=D(18),RightMargin=D(18),BottomMargin=D(12)});
        root.AddView(chrome,new FrameLayout.LayoutParams(-1,-2,GravityFlags.Top));
        navScrim=new View(this){Background=new Android.Graphics.Drawables.GradientDrawable(Android.Graphics.Drawables.GradientDrawable.Orientation.TopBottom,new[]{Android.Graphics.Color.Transparent.ToArgb(),Android.Graphics.Color.ParseColor("#40090C14").ToArgb(),Android.Graphics.Color.ParseColor("#B3090C14").ToArgb()})};root.AddView(navScrim,new FrameLayout.LayoutParams(-1,D(120),GravityFlags.Bottom));
        navigation=new FloatingNavigation(this,SelectTab);root.AddView(navigation,new FrameLayout.LayoutParams(-1,D(72),GravityFlags.Bottom|GravityFlags.CenterHorizontal){LeftMargin=D(14),RightMargin=D(14),BottomMargin=D(10)});
        root.SetOnApplyWindowInsetsListener(new ShellInsets(this));UpdateChrome();root.RequestFocus();AndroidX.Core.View.ViewCompat.RequestApplyInsets(root);
    }
    internal void CycleFeature(int direction){var count=Math.Min(5,State.Home?.Items.Count??0);if(page!="Home" || count<2)return;featureIndex=(featureIndex+direction+count)%count;_=Rebuild();}
    internal void OpenSearch(){if(offline){ShowNotice(L.T("OfflineTabBlocked"));return;}Navigate("Explore",startAtTop:true);search.RequestFocus();search.PostDelayed(()=>{if(GetSystemService(InputMethodService) is InputMethodManager keyboard)keyboard.ShowSoftInput(search,ShowFlags.Implicit);},100);}
    private void LayoutChrome(){
        if(list is null)return;var home=page=="Home";
        chrome.Visibility=home?ViewStates.Gone:ViewStates.Visible;chrome.SetPadding(safeLeft,safeTop,safeRight,0);
        var top=home?0:safeTop+D(search.Visibility==ViewStates.Visible?120:62);
        var keyboard=keyboardBottom>safeBottom+D(100);
        var fontScale=Resources!.Configuration!.FontScale;
        list.SetPadding(safeLeft,top,safeRight,keyboard?(OperatingSystem.IsAndroidVersionAtLeast(30)?keyboardBottom:0)+D(12):D(MobileLayout.ContentBottomPadding((int)(safeBottom/Density),fontScale)));
        var layout=(FrameLayout.LayoutParams)navigation.LayoutParameters!;layout.Width=D(MobileLayout.NavigationWidth((int)(ContentWidthPixels/Density)));layout.LeftMargin=safeLeft+D(14);layout.RightMargin=safeRight+D(14);layout.BottomMargin=safeBottom+D(10);layout.Height=D(MobileLayout.NavigationHeight(fontScale));navigation.LayoutParameters=layout;
        navigation.Visibility=navScrim.Visibility=keyboard?ViewStates.Gone:ViewStates.Visible;
        var scrim=(FrameLayout.LayoutParams)navScrim.LayoutParameters!;scrim.Height=safeBottom+D(MobileLayout.NavigationHeight(fontScale)+50);navScrim.LayoutParameters=scrim;
    }
    private sealed class ShellInsets(MainActivity owner):Java.Lang.Object,View.IOnApplyWindowInsetsListener {
        public WindowInsets OnApplyWindowInsets(View? view,WindowInsets? insets){
            if(insets is null)return insets!;
            var oldTop=owner.safeTop;
            if(OperatingSystem.IsAndroidVersionAtLeast(30)){var edges=insets.GetInsets(WindowInsets.Type.SystemBars()|WindowInsets.Type.DisplayCutout());owner.safeLeft=edges.Left;owner.safeTop=edges.Top;owner.safeRight=edges.Right;owner.safeBottom=edges.Bottom;owner.keyboardBottom=insets.GetInsets(WindowInsets.Type.Ime()).Bottom;}
            else{owner.safeLeft=insets.SystemWindowInsetLeft;owner.safeTop=insets.SystemWindowInsetTop;owner.safeRight=insets.SystemWindowInsetRight;var bottom=insets.SystemWindowInsetBottom;owner.keyboardBottom=bottom>owner.D(100)?bottom:0;owner.safeBottom=owner.keyboardBottom>0?0:bottom;}
            owner.LayoutChrome();if(oldTop!=owner.safeTop)owner.adapter.ResizeHero();return insets;
        }
    }
    private int D(float value)=>Ui.Dp(this,value);
    private sealed class CatalogWidthChanged(MainActivity owner):Java.Lang.Object,View.IOnLayoutChangeListener{
        public void OnLayoutChange(View? view,int left,int top,int right,int bottom,int oldLeft,int oldTop,int oldRight,int oldBottom){if(right-left!=oldRight-oldLeft)owner.list.Post(()=>{if(!owner.IsDestroyed && owner.page=="Explore" && owner.grid.SpanCount!=owner.Columns)_=owner.Rebuild();});}
    }
    protected override void OnResume(){base.OnResume();active=true;State.Changed+=StateChanged;State.Store.Changed+=StoreChanged;YoruApp.Downloads.Changed+=DownloadsChanged;adapter.RefreshDownloads(list);if(connectivity is not null)connectivity.Changed+=ConnectivityChanged;connectivity?.Start();if(offline && !offlineNoticeShown){offlineNoticeShown=true;ShowNotice(L.T("OfflineNotice"));}_=Rebuild();}
    protected override void OnPause(){active=false;if(connectivity is not null)connectivity.Changed-=ConnectivityChanged;connectivity?.Stop();watchLookup.Cancel();watchDialog?.Dismiss();State.Changed-=StateChanged;State.Store.Changed-=StoreChanged;YoruApp.Downloads.Changed-=DownloadsChanged;base.OnPause();}
    protected override void OnDestroy(){watchLookup.Cancel();watchDialog?.Dismiss();watchDialog?.Dispose();watchLookup.Dispose();downloadDialog?.Dismiss();downloadDialog?.Dispose();screen.Cancel();debounce.Cancel();navigation?.Release();if(notice is not null){root?.RemoveView(notice);notice=null;}connectivity?.Release();connectivity=null;if(viewportPreDraw is not null && list?.ViewTreeObserver is {IsAlive:true} observer)observer.RemoveOnPreDrawListener(viewportPreDraw);list?.SetAdapter(null);base.OnDestroy();}
    protected override void OnNewIntent(Intent? intent){base.OnNewIntent(intent);Intent=intent;if(intent?.GetStringExtra("page")=="Downloads")Navigate("Downloads");}
    private void DownloadsChanged(){if(active)RunOnUiThread(()=>{if(!active || IsDestroyed)return;if(page=="Downloads"){if(!adapter.UpdateDownloadState(list,DownloadRows(),ViewportKey))_=Rebuild();}else if(page=="Details")adapter.RefreshDownloads(list);});}
    protected override void OnSaveInstanceState(Bundle outState){outState.PutString("page",page);outState.PutString("return",returnPage);outState.PutString("query",query);outState.PutString("anime",anime?.Id);outState.PutInt("scroll",grid.FindFirstVisibleItemPosition());outState.PutStringArrayList("expanded-downloads",expandedDownloads.ToArray());outState.PutString("offline-resume",offlineResumePage);outState.PutString("offline-return",offlineResumeReturn);outState.PutString("offline-anime",offlineResumeAnime?.Id);outState.PutString("offline-landing",offlineLanding);base.OnSaveInstanceState(outState);}
    public override void OnConfigurationChanged(Configuration newConfig){base.OnConfigurationChanged(newConfig);var position=grid.FindFirstVisibleItemPosition();grid.SpanCount=Columns;grid.SetSpanSizeLookup(new FeedAdapter.Spans(adapter,Columns));adapter.ResizeHero();LayoutChrome();_=Rebuild(position);}
    private async Task Bootstrap(){await State.Bootstrap();if(!IsDestroyed)await Rebuild();}
    private void StateChanged(){if(active)RunOnUiThread(()=>{_=Rebuild();});}
    private void StoreChanged(){if(active)RunOnUiThread(()=>{_=Rebuild();});}
    private void UpdateChrome(){
        heading.Text=page is "Home" or "Details"?"YORU":L.T(page=="More"?"MobileMore":page);
        back.Visibility=page=="Details" || MobileNavigation.IsMoreChild(page)?ViewStates.Visible:ViewStates.Gone;
        search.Visibility=offline || page is "Settings" or "More" or "Downloads"?ViewStates.Gone:ViewStates.Visible;
        search.Enabled=!offline;
        search.Hint=L.T(page=="Details"?"MobileEpisodeSearch":"SearchPlaceholder");
        // Home and Explore need the network; keep them visible but clearly inert.
        navigation.SetEnabled("Home",OfflineNavigation.TabEnabled(!offline,"Home"));
        navigation.SetEnabled("Explore",OfflineNavigation.TabEnabled(!offline,"Explore"));
        var selected=MobileNavigation.SelectedTab(page,returnPage);
        navigation.Select(selected);LayoutChrome();
    }
    private void SelectTab(string target){
        if(offline && OfflineNavigation.OnlineOnly(target)){ShowNotice(L.T("OfflineTabBlocked"));return;}
        if(target is "Home" or "Explore"){
            if(page==target){ScrollTabToTop();return;}
            Navigate(target,startAtTop:true);return;
        }
        if(page!=target)Navigate(target);
    }
    private void ScrollTabToTop(){
        homeLaunchPending=false;tabTopPending=null;pendingViewport=null;deferCatalogPrefetch=false;viewports.Remove(ViewportKey);positions[page]=0;if(page=="Explore" && catalog is not null)catalog.ScrollPosition=0;
        if(scrollingTabTop)return;
        list.StopScroll();if(!list.CanScrollVertically(-1))return;
        if(!Android.Animation.ValueAnimator.AreAnimatorsEnabled()){grid.ScrollToPositionWithOffset(0,0);return;}
        scrollingTabTop=true;
        grid.StartSmoothScroll(new HomeSmoothScroller(this){TargetPosition=0});
    }
    private void Navigate(string target,bool startAtTop=false){
        watchLookup.Cancel();watchDialog?.Dismiss();checkingEpisode=null;
        target=MobileNavigation.NormalizePage(target);
        if(offline && OfflineNavigation.OnlineOnly(target)){ShowNotice(L.T("OfflineTabBlocked"));return;}
        moreTransitionPending=page=="More" && MobileNavigation.IsMoreChild(target) || MobileNavigation.IsMoreChild(page) && target=="More";
        list.Animate()!.Cancel();list.Alpha=1;list.StopScroll();scrollingTabTop=false;
        homeLaunchPending=startAtTop && target=="Home";tabTopPending=startAtTop?target:null;detailsRevision++;SaveViewport();pendingViewport=null;deferCatalogPrefetch=false;
        if(startAtTop)positions[target]=0;
        screen.Cancel();screen.Dispose();screen=new();debounce.Cancel();page=target;episodeQuery="";detailsError=null;settingSearch=true;search.Text=page=="Explore"?query:"";settingSearch=false;
        var catalogQuery=page=="Explore"?query:page=="Genres"?genre:"";
        catalog=Paged?(page==returnPage && returnCatalog?.Query==catalogQuery?returnCatalog:State.Catalog(catalogQuery)):null;
        if(startAtTop){viewports.Remove(ViewportKey);if(catalog is not null)catalog.ScrollPosition=0;}
        else if(viewports.Find(ViewportKey) is { } anchor){pendingViewport=new(ViewportKey,anchor);deferCatalogPrefetch=Paged;}
        UpdateChrome();_=Rebuild(positions.GetValueOrDefault(page));if(Paged && catalog?.Items.Count==0)_=LoadMore();HideKeyboard();
    }
    private void SearchChanged(){
        if(settingSearch)return;if(page=="Details"){episodeQuery=search.Text??"";_=Rebuild();return;}
        debounce.Cancel();debounce.Dispose();debounce=new();_=DebouncedSearch(search.Text??"",debounce.Token);
    }
    private async Task DebouncedSearch(string value,CancellationToken ct){try{await Task.Delay(300,ct);if(IsDestroyed || ct.IsCancellationRequested)return;SaveViewport();pendingViewport=null;deferCatalogPrefetch=false;query=value;page="Explore";catalog=State.Catalog(value);UpdateChrome();await Rebuild(catalog.ScrollPosition);await LoadMore();}catch(OperationCanceledException){}}
    private async Task LoadMore(){
        var current=catalog;if(current is null || current.Loading || current.Complete || !Paged)return;var token=screen.Token;
        var loading=current.Load(token);await Rebuild();try{await loading;}catch(OperationCanceledException){return;}
        if(!IsDestroyed && ReferenceEquals(current,catalog) && !token.IsCancellationRequested)await Rebuild();
    }
    private IEnumerable<Anime> Known()=>State.Home?.Items.Concat(State.Catalog("").Items).DistinctBy(item=>item.Id)??State.Catalog("").Items;
    private FeedRow Poster(Anime value)=>new("anime:"+value.Id,RowKind.Poster,value.Name,Meta(value),value,Saved:State.Store.IsFavorite(value.Id));
    private static string Meta(Anime value){var pieces=new[]{AnimeMetadata.Year(value),AnimeMetadata.Genres(value).FirstOrDefault()??value.Raw.Text("type"),AnimeMetadata.Rating(value)>0?"\u2605 "+AnimeMetadata.Rating(value).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture):""};return string.Join(" \u00B7 ",pieces.Where(piece=>piece.Length>0));}
    private async Task Rebuild(int? scroll=null){
        if(IsDestroyed || adapter is null)return;var revision=++feedRevision;var currentPage=page;var context=ViewportKey;var rows=new List<FeedRow>();
        if(grid.SpanCount!=Columns){grid.SpanCount=Columns;grid.SetSpanSizeLookup(new FeedAdapter.Spans(adapter,Columns));adapter.ResizeHero();}
        if(page=="Home") {
            if(State.Home?.Items.ElementAtOrDefault(featureIndex % Math.Max(1,State.Home.Items.Count)) is { } feature){rows.Add(new("featured",RowKind.Hero,feature.Name,string.IsNullOrWhiteSpace(feature.Story)?L.T("StoryFallback"):feature.Story,feature,Saved:State.Store.IsFavorite(feature.Id)));rows.Add(new("continue-heading",RowKind.Heading,L.T("History")));foreach(var entry in State.Store.History.Take(2))rows.Add(HistoryRow(entry));if(State.Store.History.Count==0)rows.Add(new("history-empty",RowKind.Message,L.T("HistoryWaiting"),L.T("HistoryHint")));rows.Add(new("popular-heading",RowKind.Heading,L.T("Popular")));rows.AddRange(State.Home.Items.OrderByDescending(item=>item.Raw.Number("views")).Take(12).Select(Poster));}
            else if(State.Ready){rows.Add(new("home-search",RowKind.Option,L.T("MobileSearch"),Action:"open-search"));rows.Add(new("home-empty",RowKind.Message,L.T("MobileEmpty"),L.T("MobileRetry"),Action:"retry-home"));}
            else{rows.Add(new("hero-skeleton",RowKind.HeroSkeleton,L.T("MobileLoading")));rows.Add(new("popular-heading",RowKind.Heading,L.T("Popular")));for(var i=0;i<Columns*2;i++)rows.Add(new("skeleton:"+i,RowKind.Skeleton));}
            if(State.HomeError is not null)rows.Add(new("home-error",RowKind.Message,State.HomeError,L.T("MobileRetry"),Action:"retry-home"));
        } else if(page=="History") {
            rows.Add(new("history-title",RowKind.Heading,L.T("History")));if(State.Store.History.Count>0)rows.Add(new("clear",RowKind.Option,L.T("ClearHistory"),Action:"clear-history"));rows.AddRange(State.Store.History.Select(HistoryRow));
        } else if(page=="MyList") {rows.Add(new("saved-title",RowKind.Heading,L.T("MyList")));rows.AddRange(State.Store.Favorites.Select(Poster));}
        else if(page=="More") {
            rows.Add(new("more-intro",RowKind.Message,L.T("MobileYourSpace"),L.T("MobileMoreHint")));
            rows.Add(new("library-heading",RowKind.Heading,L.T("MobileLibrary")));
            foreach(var name in new[]{"MyList","Genres","Seasonal","Downloads"})rows.Add(new("more:"+name,RowKind.Option,L.T(name),L.T("MobileHint"+name),Action:"navigate:"+name));
            rows.Add(new("preferences-heading",RowKind.Heading,L.T("MobilePreferences")));rows.Add(new("more:Settings",RowKind.Option,L.T("Settings"),L.T("MobileHintSettings"),Action:"navigate:Settings"));
        }
        else if(page=="Downloads") {
            rows.AddRange(DownloadRows());
        }
        else if(page=="Settings") {
            rows.Add(new("about",RowKind.About,"YORU",L.T("MobileVersion")));rows.Add(new("language",RowKind.Option,L.T("Language"),L.T(L.Language=="ar"?"MobileArabic":"MobileEnglish"),Action:"language"));rows.Add(new("settings-history",RowKind.Option,L.T("ClearHistory"),Action:"clear-history"));
        } else if(page=="Details" && anime is not null) {
            rows.Add(new("detail-hero:"+anime.Id,RowKind.Hero,anime.Name,string.IsNullOrWhiteSpace(anime.Story)?L.T("StoryFallback"):anime.Story,anime,Saved:State.Store.IsFavorite(anime.Id)));
            rows.Add(new("detail-story",RowKind.Message,L.T("MobileSynopsis"),string.IsNullOrWhiteSpace(anime.Story)?L.T("StoryFallback"):anime.Story));
            rows.Add(new("episode-count",RowKind.Heading,episodes.Count==0 && (detailsLoading || detailsError is not null)?L.T("MobileEpisodesHeading"):L.T("MobileEpisodes",episodes.Count)));
            if(detailsLoading && episodes.Count==0)for(var i=0;i<3;i++)rows.Add(new("episodes-loading:"+anime.Id+":"+i,RowKind.EpisodeSkeleton));
            foreach(var episode in episodes.Where(item=>string.IsNullOrWhiteSpace(episodeQuery)||(item.Name+" "+item.Id+" "+item.Subtitle).Contains(episodeQuery,StringComparison.OrdinalIgnoreCase)))rows.Add(new("episode:"+episode.Id,RowKind.Episode,episode.Name,episode.Subtitle,anime,episode));
            if(detailsError is not null)rows.Add(new("details-error",RowKind.Message,detailsError,L.T("MobileRetry"),Action:"retry-details"));
        } else if(Paged) {
            rows.Add(new("catalog-title",RowKind.Heading,L.T(page=="Explore"?"ExploreAnime":page)));
            if(page=="Explore")rows.Add(new("sort",RowKind.Option,L.T("MobileSort"),sort?L.T("MobileSorted"):"",Action:"sort"));
            IEnumerable<Anime> items=catalog?.Items??Array.Empty<Anime>();
            if(page=="Seasonal"){var year=Known().Select(AnimeMetadata.Year).OrderDescending(StringComparer.Ordinal).FirstOrDefault()??"";items=items.Where(value=>AnimeMetadata.Year(value)==year);}
            if(page=="Genres") {
                rows.Add(new("genre-picker",RowKind.Option,string.IsNullOrEmpty(genre)?L.T("Genres"):genre,Action:"genres"));if(genre.Length>0)items=items.Where(value=>AnimeMetadata.Genres(value).Contains(genre));
            }
            if(sort && page=="Explore")items=items.OrderByDescending(AnimeMetadata.Rating);
            rows.AddRange(items.Select(Poster));
            if(catalog?.Loading==true){if(!items.Any())for(var i=0;i<Columns*2;i++)rows.Add(new("skeleton:"+i,RowKind.Skeleton));rows.Add(new("catalog-loading",RowKind.Loading,L.T("LoadingMore")));}
            else if(catalog?.Error is { } error)rows.Add(new("catalog-error",RowKind.Message,error,L.T("MobileRetry"),Action:"retry-catalog"));
            else if(catalog?.Complete==true)rows.Add(new("catalog-done",RowKind.Message,L.T("MobileDone")));
        }
        if(rows.Count==0 || rows.All(row=>row.Kind==RowKind.Heading))rows.Add(new("empty",RowKind.Message,L.T("MobileEmpty")));
        await adapter.Update(rows,context);if(revision!=feedRevision || currentPage!=page || context!=ViewportKey || IsDestroyed)return;
        UpdateChrome();
        if(pendingViewport is { } pending && pending.Context==context)grid.ScrollToPositionWithOffset(pending.Anchor.Resolve(adapter.RowKeys),pending.Anchor.OffsetPixels);
        else if(scroll is { } position)grid.ScrollToPositionWithOffset(Math.Max(0,position),0);navigation.EnsureBackdrop(list);
        if(moreTransitionPending){moreTransitionPending=false;if(Android.Animation.ValueAnimator.AreAnimatorsEnabled()){list.Alpha=.94f;list.Animate()!.Alpha(1).SetDuration(140).Start();}}
        if(page=="Home" && homeLaunchPending){list.StopScroll();grid.ScrollToPositionWithOffset(0,0);list.Post(()=>{if(revision!=feedRevision || IsDestroyed || page!="Home" || !homeLaunchPending)return;grid.ScrollToPositionWithOffset(0,0);if(State.Home?.Items.Count>0 || State.Ready)homeLaunchPending=false;});}
        if(tabTopPending==page){
            grid.ScrollToPositionWithOffset(0,0);
            list.Post(()=>{if(revision!=feedRevision || IsDestroyed || tabTopPending!=page)return;grid.ScrollToPositionWithOffset(0,0);if((!Paged || catalog is {Items.Count:>0} || catalog?.Complete==true || catalog?.Error is not null) && grid.FindFirstVisibleItemPosition()==0)tabTopPending=null;});
        }
        list.Post(()=>{if(revision==feedRevision && !IsDestroyed && !deferCatalogPrefetch && pendingViewport is null && tabTopPending is null && Paged && catalog is {Loading:false,Complete:false,Error:null} && grid.FindLastVisibleItemPosition()>=adapter.ItemCount-8)_=LoadMore();});
    }
    private static FeedRow HistoryRow(WatchProgress entry)=>new("history:"+entry.Anime.Id,RowKind.History,entry.Anime.Name,entry.Episode.Name+" \u00B7 "+TimeSpan.FromSeconds(entry.Seconds).ToString(@"hh\:mm\:ss"),entry.Anime,Progress:entry);
    internal void HandleRow(FeedRow row){
        if(row.Group is { } group){if(!expandedDownloads.Add(group.Anime.Id))expandedDownloads.Remove(group.Anime.Id);_=Rebuild();return;}
        if(row.Download is { } download){if(download.Status==DownloadStatus.Completed)PlayDownloaded(download);else ShowDownloadMenu(download);return;}
        if(row.Kind==RowKind.Episode && row.Episode is not null){if(page=="Details" && row.Anime?.Id==anime?.Id)_=ChooseEpisode(row.Episode);return;}
        if(row.Progress is not null){
            if(offline){var saved=YoruApp.Downloads.Find(row.Progress.Anime.Id,row.Progress.Episode.Id);if(saved is {Status:DownloadStatus.Completed}){PlayDownloaded(saved);return;}ShowNotice(L.T("OfflineNotDownloaded"));return;}
            OpenAnime(row.Progress.Anime,true,row.Progress.Episode.Id);return;
        }
        if(row.Anime is not null){
            if(offline){ShowNotice(L.T("OfflineNotDownloaded"));return;}
            OpenAnime(row.Anime,false);return;
        }
        var action=row.Action;if(action.StartsWith("navigate:")){Navigate(action[9..]);return;}
        switch(action){case "open-search":OpenSearch();break;case "clear-history":ConfirmClear();break;case "language":ChooseLanguage();break;case "genres":ChooseGenre();break;case "sort":sort=!sort;_=Rebuild();break;case "retry-home":_=RetryHome();break;case "retry-catalog":_=LoadMore();break;case "retry-details":if(anime is not null)_=LoadDetails(anime,false);break;}
    }
    internal async void ToggleFavorite(Anime value){try{await State.Store.ToggleFavorite(value);}catch(Exception e){Ui.Error(this,e);}}
    internal void HistoryMenu(WatchProgress progress){var dialog=Ui.Dialog(this,progress.Anime.Name);dialog.SetItems(new[]{L.T("RemoveHistory")},async(_,_)=>{try{await State.Store.RemoveHistory(progress.Anime.Id);}catch(Exception e){Ui.Error(this,e);}});dialog.Show();}
    private void ConfirmClear(){var dialog=Ui.Dialog(this,L.T("ClearHistory"));dialog.SetMessage(L.T("MobileClearConfirm"));dialog.SetNegativeButton(L.T("MobileCancel"),(_,_)=>{});dialog.SetPositiveButton(L.T("ClearHistory"),async(_,_)=>{try{await State.Store.ClearHistory();}catch(Exception e){Ui.Error(this,e);}});dialog.Show();}
    private void ChooseLanguage(){var builder=Ui.Dialog(this,L.T("Language"));builder.SetSingleChoiceItems(new[]{L.T("MobileEnglish"),L.T("MobileArabic")},L.Language=="ar"?1:0,async(sender,e)=>{if(sender is AlertDialog dialog)dialog.Dismiss();await State.Store.SetLanguage(e.Which==1?"ar":"en");Recreate();});builder.Show();}
    private void ChooseGenre(){var genres=Known().SelectMany(AnimeMetadata.Genres).Distinct().Order(StringComparer.Ordinal).ToArray();var dialog=Ui.Dialog(this,L.T("Genres"));dialog.SetItems(genres,(_,e)=>{genre=genres[e.Which];catalog=State.Catalog(genre);_=Rebuild(0);_=LoadMore();});dialog.Show();}
    private async Task RetryHome(){await State.Bootstrap(true);await Rebuild();}
    internal void OpenAnime(Anime value,bool watch,string? episode=null){
        if(offline){ShowNotice(L.T("OfflineNotDownloaded"));return;}
        watchLookup.Cancel();watchDialog?.Dismiss();checkingEpisode=null;
        moreTransitionPending=false;list.Animate()!.Cancel();list.Alpha=1;list.StopScroll();scrollingTabTop=false;homeLaunchPending=false;tabTopPending=null;
        SaveViewport();pendingViewport=null;deferCatalogPrefetch=false;
        if(page!="Details"){returnPage=page;returnCatalog=Paged?catalog:null;}
        screen.Cancel();screen.Dispose();screen=new();debounce.Cancel();
        adapter.PrepareDetails(list,value.Id);
        anime=value;page="Details";episodes=State.Episodes.Cached(value.Id);detailsError=null;
        settingSearch=true;search.Text="";settingSearch=false;episodeQuery="";
        UpdateChrome();HideKeyboard();_=LoadDetails(value,watch,episode);
    }
    private async Task LoadDetails(Anime value,bool watch,string? episode=null){
        // Capture identity/token before the first UI await: late A completions must
        // never mutate a later B visit, including an A -> B -> A sequence.
        var revision=++detailsRevision;var token=screen.Token;
        bool Current()=>!IsDestroyed && revision==detailsRevision && page=="Details" && anime?.Id==value.Id && !token.IsCancellationRequested;
        episodes=State.Episodes.Cached(value.Id);detailsLoading=episodes.Count==0;detailsError=null;
        await Rebuild(0);if(!Current())return;
        try{
            await State.Cache.Initialize(token);if(!Current())return;
            async Task Metadata(){
                try{var info=await State.Cache.Details(value.Id,token);if(!Current())return;anime=info;await Rebuild();}
                catch(OperationCanceledException){}catch(Exception e){if(!Current())return;detailsError=L.Error(e);await Rebuild();}
            }
            async Task EpisodeList(){
                try{
                    var loaded=await State.Episodes.Load(value.Id,token);if(!Current())return;
                    episodes=loaded;detailsLoading=false;await Rebuild();
                    if(!Current() || !watch)return;
                    var selected=episodes.FirstOrDefault(item=>item.Id==(episode??State.Store.History.FirstOrDefault(item=>item.Anime.Id==value.Id)?.Episode.Id))??episodes.FirstOrDefault();
                    if(selected is not null)await ChooseEpisode(selected);
                }
                catch(OperationCanceledException){}catch(Exception e){if(!Current())return;detailsLoading=false;detailsError=L.Error(e);await Rebuild();}
            }
            await Task.WhenAll(Metadata(),EpisodeList());
        }
        catch(OperationCanceledException){}catch(Exception e){if(!Current())return;detailsLoading=false;detailsError=L.Error(e);await Rebuild();}
    }
    private async Task ChooseEpisode(Episode episode){
        if(anime is null || checkingEpisode==anime.Id+"\0"+episode.Id)return;var currentAnime=anime;var currentEpisodes=episodes.ToArray();
        watchLookup.Cancel();watchDialog?.Dismiss();watchDialog?.Dispose();watchLookup.Dispose();watchLookup=new();var cancellation=watchLookup;var ct=cancellation.Token;var revision=++watchRevision;checkingEpisode=currentAnime.Id+"\0"+episode.Id;
        bool Current()=>!IsDestroyed && active && revision==watchRevision && page=="Details" && anime?.Id==currentAnime.Id && !ct.IsCancellationRequested;
        var progress=ServerUi.Checking(this);watchDialog=progress;var completed=false;progress.DismissEvent+=(_,_)=>{if(!completed && !cancellation.IsCancellationRequested){try{cancellation.Cancel();}catch(ObjectDisposedException){}}};
        try{
            if(!ServerUi.Connected(this))throw new ServersUnavailable(true);
            var ready=await State.Servers.Select(currentAnime.Id,episode.Id,false,ct);if(!Current())return;
            completed=true;progress.Dismiss();State.Playback=new(currentAnime,episode,currentEpisodes,ready.Source.Label,Validated:ready);
            var intent=new Intent(this,typeof(PlayerActivity));intent.PutExtra("anime",currentAnime.Id);intent.PutExtra("episode",episode.Id);StartActivity(intent);
        }catch(OperationCanceledException){}catch(Exception error){
            if(!Current())return;completed=true;progress.Dismiss();
            watchDialog=ServerUi.Unavailable(this,()=>{if(page=="Details" && anime?.Id==currentAnime.Id){checkingEpisode=null;State.Servers.Invalidate(currentAnime.Id,episode.Id);_=ChooseEpisode(episode);}},()=>{},!ServerUi.Connected(this)||ServerValidation.IsNetwork(error));
        }finally{if(revision==watchRevision){checkingEpisode=null;if(!completed){completed=true;if(!IsDestroyed && progress.Handle!=IntPtr.Zero)progress.Dismiss();}}}
    }
    internal void TrackDownloadDialog(Dialog dialog){downloadDialog?.Dismiss();downloadDialog?.Dispose();downloadDialog=dialog;}
    private List<FeedRow> DownloadRows(){
        var rows=new List<FeedRow>{new("downloads-title",RowKind.Heading,L.T("Downloads"))};
        if(offline)rows.Add(new("offline-mode",RowKind.Message,L.T("OfflineMode"),L.T("OfflineDownloadsHint")));
        var groups=DownloadGroups.Build(YoruApp.Downloads.Items);
        if(groups.Count==0)rows.Add(new("downloads-empty",RowKind.Message,L.T("DownloadEmpty"),L.T("DownloadEmptyHint")));
        foreach(var group in groups){var expanded=expandedDownloads.Contains(group.Anime.Id);rows.Add(new("download-group:"+group.Anime.Id,RowKind.DownloadGroup,group.Anime.Name,Anime:group.Anime,Group:group,Expanded:expanded));
            if(expanded)foreach(var item in group.Episodes)rows.Add(new("download:"+item.Id,RowKind.Download,item.EpisodeOrder>0?L.T("MobileEpisodeNumber",item.EpisodeOrder)+" · "+item.Episode.Name:item.Episode.Name,Anime:item.Anime,Episode:item.Episode,Download:item));
        }
        return rows;
    }
    internal void OpenDownload(Anime value,Episode episode,int order){
        if(offline){ShowNotice(L.T("OfflineDownloadUnavailable"));return;}
        var existing=YoruApp.Downloads.Find(value.Id,episode.Id);if(existing is {Active:true} || existing?.Status==DownloadStatus.Completed){ShowDownloadMenu(existing);return;}
        new DownloadPopup(this,value,episode,order).Show();
    }
    internal void DownloadEpisode(FeedRow row){if(row.Anime is not null && row.Episode is not null)OpenDownload(row.Anime,row.Episode,episodes.ToList().FindIndex(item=>item.Id==row.Episode.Id)+1);}
    internal void ShowDownloadMenu(EpisodeDownload item)=>TrackDownloadDialog(DownloadMenus.Show(this,item));
    internal void PlayDownloaded(EpisodeDownload item){
        var request=DownloadMenus.Offline(item);if(request is null){Toast.MakeText(this,L.T("DownloadMissing"),ToastLength.Long)?.Show();return;}
        State.Playback=request;var intent=new Intent(this,typeof(PlayerActivity));intent.PutExtra("download",item.Id);intent.PutExtra("anime",item.Anime.Id);intent.PutExtra("episode",item.Episode.Id);StartActivity(intent);
    }
    private void HideKeyboard(){if(GetSystemService(InputMethodService) is InputMethodManager keyboard)keyboard.HideSoftInputFromWindow(search.WindowToken,HideSoftInputFlags.None);search.ClearFocus();}
    private void GoBack(){if(MobileNavigation.BackTarget(page,returnPage) is { } parent){if(offline && OfflineNavigation.OnlineOnly(parent)){Finish();return;}Navigate(parent);}else Finish();}
    private void ConnectivityChanged(bool online){if(IsDestroyed)return;RunOnUiThread(()=>ApplyConnectivity(online));}
    private void ApplyConnectivity(bool online){
        if(IsDestroyed || online==!offline)return;
        if(online)LeaveOffline();else EnterOffline();
    }
    // A drop in the middle of browsing keeps the user's place, moves them to the
    // downloads they can still watch, and explains what happened once.
    private void EnterOffline(){
        offline=true;
        var stranded=OfflineNavigation.OnlineOnly(page);
        if(stranded){offlineResumePage=page;offlineResumeReturn=returnPage;offlineResumeAnime=anime;}
        watchLookup.Cancel();watchDialog?.Dismiss();checkingEpisode=null;
        if(stranded){offlineLanding="Downloads";Navigate("Downloads",startAtTop:true);}
        else{offlineLanding=null;UpdateChrome();_=Rebuild();}
        ShowNotice(L.T("OfflineNotice"));
    }
    // Reconnection re-enables everything immediately and hands back the page the
    // user was on, unless they deliberately moved somewhere else while offline.
    private void LeaveOffline(){
        offline=false;
        var target=page==offlineLanding?OfflineNavigation.RestorePage(true,offlineResumePage):null;
        var parent=offlineResumeReturn;var remembered=offlineResumeAnime;
        offlineResumePage=null;offlineResumeReturn=null;offlineResumeAnime=null;offlineLanding=null;
        UpdateChrome();
        if(target is null)_=Rebuild();
        else if(target=="Details" && remembered is not null){OpenAnime(remembered,false);returnPage=parent??"Home";UpdateChrome();}
        else Navigate(target,startAtTop:true);
        ShowNotice(L.T("OnlineBack"));
        if(!State.Ready)_=Bootstrap();
    }
    private void ShowNotice(string text){
        if(IsDestroyed || root is null || string.IsNullOrWhiteSpace(text))return;
        // A failed hand-over can flip twice within a second; the second pass must
        // not repeat the same message on top of the one still showing.
        var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if(text==lastNotice && now-lastNoticeAt<6000)return;
        lastNotice=text;lastNoticeAt=now;
        if(notice is not null){root.RemoveView(notice);notice=null;}
        var pill=new LinearLayout(this){Orientation=Orientation.Horizontal};
        pill.SetGravity(GravityFlags.CenterVertical);pill.Clickable=false;
        pill.Background=Ui.OutlinedShape(Android.Graphics.Color.ParseColor("#F2151827"),D(22),Android.Graphics.Color.ParseColor("#66A78BFA"));
        pill.Elevation=D(10);pill.SetPadding(D(16),D(10),D(16),D(10));
        var dot=new View(this){Background=Ui.Shape(Ui.Selected,D(5))};
        pill.AddView(dot,new LinearLayout.LayoutParams(D(9),D(9)){MarginEnd=D(10)});
        var label=Ui.Label(this,text,13);label.SetMaxLines(3);pill.AddView(label,new LinearLayout.LayoutParams(-2,-2));
        root.AddView(pill,new FrameLayout.LayoutParams(-2,-2,GravityFlags.Bottom|GravityFlags.CenterHorizontal){BottomMargin=safeBottom+D(96)});
        notice=pill;pill.Alpha=0f;pill.TranslationY=D(10);
        pill.Animate()!.Alpha(1f).TranslationY(0f).SetDuration(180).Start();
        pill.PostDelayed(()=>{if(notice!=pill || IsDestroyed)return;pill.Animate()!.Alpha(0f).TranslationY(D(10)).SetDuration(200).Start();},2800);
        pill.PostDelayed(()=>{if(notice!=pill)return;root.RemoveView(pill);notice=null;},3050);
    }
    private sealed class MoreScroll(Action load,Action<int> state):RecyclerView.OnScrollListener{
        public override void OnScrolled(RecyclerView recyclerView,int dx,int dy){if(dy>0)load();}
        public override void OnScrollStateChanged(RecyclerView recyclerView,int newState){state(newState);}
    }
}
