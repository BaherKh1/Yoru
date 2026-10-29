using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using Yoru;
using Yoru.Core;
using DownloadStatus=Yoru.Core.DownloadStatus;
namespace Yoru.AndroidApp;

internal enum RowKind {Hero,Poster,Heading,History,Episode,Option,Message,Loading,About,Skeleton,HeroSkeleton,EpisodeSkeleton,Download,DownloadGroup}
internal sealed record FeedRow(string Key,RowKind Kind,string Title="",string Subtitle="",Anime? Anime=null,Episode? Episode=null,WatchProgress? Progress=null,bool Saved=false,string Action="",EpisodeDownload? Download=null,AnimeDownloads? Group=null,bool Expanded=false);

internal sealed class FeedAdapter(MainActivity owner) : RecyclerView.Adapter
{
    private List<FeedRow> rows=new();private int revision;
    public string ContextKey{get;private set;}="";
    public IReadOnlyList<string> RowKeys{get;private set;}=Array.Empty<string>();
    public override int ItemCount=>rows.Count;
    public FeedRow Row(int position)=>rows[position];
    public async Task Update(List<FeedRow> next,string context){
        var version=++revision;var previous=rows;
        var compactChanged=ContextKey.StartsWith("Explore\0",StringComparison.Ordinal)!=context.StartsWith("Explore\0",StringComparison.Ordinal);
        var result=await Task.Run(()=>DiffUtil.CalculateDiff(new Changes(previous,next,compactChanged),false));
        if(version!=revision || owner.IsDestroyed)return;
        rows=next;ContextKey=context;RowKeys=next.Select(row=>row.Key).ToArray();result.DispatchUpdatesTo(this);
    }
    public void ResizeHero(){if(ItemCount>0)NotifyItemRangeChanged(0,ItemCount);}
    public bool UpdateDownloadState(RecyclerView list,List<FeedRow> next,string context){
        if(ContextKey!=context || !RowKeys.SequenceEqual(next.Select(row=>row.Key)))return false;
        ++revision;rows=next;
        for(var i=0;i<list.ChildCount;i++){var child=list.GetChildAt(i)!;var index=list.GetChildAdapterPosition(child);if(index>=0 && index<rows.Count && list.GetChildViewHolder(child) is Holder holder && rows[index].Kind is RowKind.DownloadGroup or RowKind.Download)holder.Bind(rows[index]);}
        return true;
    }
    public void RefreshDownloads(RecyclerView list){for(var i=0;i<list.ChildCount;i++){var position=list.GetChildAdapterPosition(list.GetChildAt(i)!);if(position>=0 && position<rows.Count && rows[position].Kind==RowKind.Episode)NotifyItemChanged(position);}}
    public void PrepareDetails(RecyclerView view,string animeId){for(var i=0;i<view.ChildCount;i++)if(view.GetChildAt(i) is { } child && view.GetChildViewHolder(child) is Holder cell)cell.PrepareDetails(animeId);}
    public override int GetItemViewType(int position)=>(int)rows[position].Kind;
    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent,int viewType)=>new Holder(owner,(RowKind)viewType);
    public override void OnBindViewHolder(RecyclerView.ViewHolder holder,int position)=>((Holder)holder).Bind(rows[position]);
    public override void OnViewRecycled(Java.Lang.Object holder){if(holder is Holder cell)cell.Cancel();base.OnViewRecycled(holder);}
    private sealed class Changes(List<FeedRow> before,List<FeedRow> after,bool compactChanged) : DiffUtil.Callback {
        public override int OldListSize=>before.Count;public override int NewListSize=>after.Count;
        public override bool AreItemsTheSame(int oldPosition,int newPosition)=>before[oldPosition].Key==after[newPosition].Key;
        public override bool AreContentsTheSame(int oldPosition,int newPosition)=>!(compactChanged && after[newPosition].Kind==RowKind.Poster) && before[oldPosition]==after[newPosition];
    }
    internal sealed class Spans(FeedAdapter adapter,int count):GridLayoutManager.SpanSizeLookup{public override int GetSpanSize(int position)=>position<adapter.ItemCount && adapter.Row(position).Kind is RowKind.Poster or RowKind.Skeleton?1:count;}

    private sealed class Holder : RecyclerView.ViewHolder
    {
        private readonly MainActivity owner;private readonly RowKind kind;private readonly FrameLayout frame;private readonly LinearLayout textPanel;
        private readonly TextView title,subtitle;private readonly ImageView? image,optionIcon;private readonly ImageButton? save;private readonly ProgressBar? progress;private readonly CinematicHero? hero;private readonly LinearLayout? skeletonHeader;
        private FeedRow? row;private CancellationTokenSource? loading;
        private readonly View? divider;private readonly ImageButton? downloadButton;private readonly ProgressBar? downloadProgress;
        private readonly DownloadAnimeCard? downloadCard;
        private int D(float value)=>Ui.Dp(owner,value);
        public Holder(MainActivity owner,RowKind kind):base(new FrameLayout(owner)) {
            this.owner=owner;this.kind=kind;frame=(FrameLayout)ItemView;
            var full=kind is RowKind.Hero or RowKind.HeroSkeleton;
            var card=kind is RowKind.Poster or RowKind.Skeleton;
            frame.LayoutParameters=new RecyclerView.LayoutParams(-1,-2){LeftMargin=D(full?0:card?6:18),RightMargin=D(full?0:card?6:18),TopMargin=D(full?0:kind==RowKind.Heading?14:kind==RowKind.Option?2:6),BottomMargin=D(full?0:kind==RowKind.Option?2:6)};
            frame.Click+=(_,_)=>{if(row is not null)owner.HandleRow(row);};
            frame.Background=Ui.Shape(kind is RowKind.Heading or RowKind.Message or RowKind.Loading or RowKind.Poster?Ui.Background:Ui.Panel,D(full?0:16));frame.ClipToOutline=true;
            frame.Foreground=new RippleDrawable(Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#168B5CF6")),null,null);
            textPanel=Ui.Vertical(owner,card?2:kind==RowKind.Option?0:kind==RowKind.Heading?0:16);
            title=Ui.Label(owner,"",kind==RowKind.Heading?21:kind==RowKind.Message?17:14);title.SetTypeface(null,TypefaceStyle.Bold);title.SetMaxLines(card?2:3);title.Ellipsize=Android.Text.TextUtils.TruncateAt.End;
            subtitle=Ui.Label(owner,"",kind==RowKind.Option?11:12,Ui.Muted);subtitle.SetMaxLines(2);subtitle.Ellipsize=Android.Text.TextUtils.TruncateAt.End;
            if(kind==RowKind.DownloadGroup){
                frame.Background=null;frame.ClipToOutline=false;downloadCard=new DownloadAnimeCard(owner);frame.AddView(downloadCard,new FrameLayout.LayoutParams(-1,-2));
            } else if(kind==RowKind.Hero) {
                hero=new CinematicHero(owner);frame.AddView(hero,new FrameLayout.LayoutParams(-1,-1));
            } else if(kind is RowKind.Skeleton or RowKind.HeroSkeleton or RowKind.EpisodeSkeleton) {
                frame.AddView(new LoadingSkeleton(owner,full,kind==RowKind.EpisodeSkeleton),new FrameLayout.LayoutParams(-1,-2));
                frame.ImportantForAccessibility=ImportantForAccessibility.No;
                if(full){skeletonHeader=new LinearLayout(owner){Orientation=Orientation.Horizontal,LayoutDirection=LayoutDirection.Ltr};skeletonHeader.SetGravity(GravityFlags.CenterVertical);skeletonHeader.SetPadding(D(20),0,D(16),0);var logo=new ImageView(owner);logo.SetImageResource(Resource.Drawable.yoru_mark);skeletonHeader.AddView(logo,new LinearLayout.LayoutParams(D(36),D(36)));var word=Ui.Label(owner,"YORU",22);word.SetTypeface(null,TypefaceStyle.Bold);skeletonHeader.AddView(word,new LinearLayout.LayoutParams(0,-1,1){LeftMargin=D(10)});skeletonHeader.AddView(StreamActions.Button(owner,"Search",L.T("MobileSearch"),owner.OpenSearch),new LinearLayout.LayoutParams(D(48),D(48)));frame.AddView(skeletonHeader,new FrameLayout.LayoutParams(-1,D(60),GravityFlags.Top));}
            } else if(kind==RowKind.Poster){
                var content=Ui.Vertical(owner);content.SetPadding(D(4),0,D(4),0);
                var art=new FrameLayout(owner){Background=Ui.Shape(Color.ParseColor("#202437"),D(12)),ClipToOutline=true};
                image=new PosterImage(owner);image.SetScaleType(ImageView.ScaleType.CenterCrop);art.AddView(image,new FrameLayout.LayoutParams(-1,-2));content.AddView(art);
                var scale=Math.Clamp(owner.Resources!.Configuration!.FontScale,1,2);
                textPanel.AddView(title,new LinearLayout.LayoutParams(-1,D(42*scale)){TopMargin=D(8)});textPanel.AddView(subtitle,new LinearLayout.LayoutParams(-1,D(32*scale)){BottomMargin=D(3)});content.AddView(textPanel);frame.AddView(content);
                save=StreamActions.Button(owner,"Add",L.T("AddList"),()=>{if(row?.Anime is not null)owner.ToggleFavorite(row.Anime);});frame.AddView(save,new FrameLayout.LayoutParams(D(48),D(48),GravityFlags.Top|GravityFlags.End){TopMargin=D(3),RightMargin=D(7),LeftMargin=D(7)});
            } else if(kind==RowKind.History) {
                var line=new LinearLayout(owner){Orientation=Orientation.Horizontal};line.SetGravity(GravityFlags.CenterVertical);line.SetPadding(D(10),D(10),D(4),D(10));
                var art=new FrameLayout(owner){Background=Ui.Shape(Ui.Background,D(10)),ClipToOutline=true};image=new ImageView(owner);image.SetScaleType(ImageView.ScaleType.CenterCrop);art.AddView(image,new FrameLayout.LayoutParams(-1,-1));line.AddView(art,new LinearLayout.LayoutParams(D(72),D(100)));
                textPanel.SetPadding(D(14),D(5),D(10),D(5));textPanel.AddView(title);textPanel.AddView(subtitle,new LinearLayout.LayoutParams(-1,-2){TopMargin=D(5)});
                progress=new ProgressBar(owner,null,Android.Resource.Attribute.ProgressBarStyleHorizontal){Max=100,ProgressTintList=Android.Content.Res.ColorStateList.ValueOf(Ui.Accent),ProgressBackgroundTintList=Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#343044"))};textPanel.AddView(progress,new LinearLayout.LayoutParams(-1,D(3)){TopMargin=D(12)});line.AddView(textPanel,new LinearLayout.LayoutParams(0,-2,1));
                var menu=StreamActions.Button(owner,"More",L.T("HistoryMenu"),()=>{if(row?.Progress is not null)owner.HistoryMenu(row.Progress);});menu.Rotation=90;line.AddView(menu,new LinearLayout.LayoutParams(D(48),D(48)));frame.AddView(line);
            } else if(kind is RowKind.Episode or RowKind.Download) {
                var line=new LinearLayout(owner){Orientation=Orientation.Horizontal};line.SetGravity(GravityFlags.CenterVertical);line.SetPadding(D(14),D(10),D(8),D(10));line.SetMinimumHeight(D(76));
                textPanel.SetPadding(0,0,D(8),0);textPanel.AddView(title);textPanel.AddView(subtitle,new LinearLayout.LayoutParams(-1,-2){TopMargin=D(4)});
                downloadProgress=new ProgressBar(owner,null,Android.Resource.Attribute.ProgressBarStyleHorizontal){Max=100,ProgressTintList=Android.Content.Res.ColorStateList.ValueOf(Ui.Accent),ProgressBackgroundTintList=Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#343044"))};
                textPanel.AddView(downloadProgress,new LinearLayout.LayoutParams(-1,D(3)){TopMargin=D(7)});
                downloadButton=PlayerGlass.Icon(owner,"Download",L.T("DownloadEpisode"),()=>{if(row?.Download is { } job)owner.ShowDownloadMenu(job);else if(row is not null)owner.DownloadEpisode(row);});
                if(kind==RowKind.Episode){line.LayoutDirection=LayoutDirection.Ltr;textPanel.LayoutDirection=L.Language=="ar"?LayoutDirection.Rtl:LayoutDirection.Ltr;textPanel.SetPadding(D(8),0,0,0);line.AddView(downloadButton,new LinearLayout.LayoutParams(D(48),D(48)));line.AddView(textPanel,new LinearLayout.LayoutParams(0,-2,1));}
                else{line.AddView(textPanel,new LinearLayout.LayoutParams(0,-2,1));line.AddView(downloadButton,new LinearLayout.LayoutParams(D(48),D(48)));}frame.AddView(line);
            } else if(kind==RowKind.Option) {
                var line=new LinearLayout(owner){Orientation=Orientation.Horizontal};line.SetGravity(GravityFlags.CenterVertical);line.SetPadding(D(14),D(10),D(12),D(10));line.SetMinimumHeight(D(68));
                var iconPanel=new FrameLayout(owner){Background=Ui.Shape(Color.ParseColor("#218B5CF6"),D(11))};optionIcon=StreamActions.Icon(owner,"More",Ui.Selected);iconPanel.AddView(optionIcon,new FrameLayout.LayoutParams(D(23),D(23),GravityFlags.Center));line.AddView(iconPanel,new LinearLayout.LayoutParams(D(40),D(40)){MarginEnd=D(13)});
                textPanel.AddView(title);textPanel.AddView(subtitle,new LinearLayout.LayoutParams(-1,-2){TopMargin=D(4)});line.AddView(textPanel,new LinearLayout.LayoutParams(0,-2,1));
                var chevron=StreamActions.Icon(owner,"Chevron",Ui.Muted,18);if(L.Language=="ar")chevron.ScaleX=-1;line.AddView(chevron,new LinearLayout.LayoutParams(D(18),D(18)){MarginStart=D(8)});frame.AddView(line);
                divider=new View(owner);divider.SetBackgroundColor(Color.ParseColor("#252A3C"));frame.AddView(divider,new FrameLayout.LayoutParams(-1,1,GravityFlags.Bottom){MarginStart=D(67),MarginEnd=D(16)});
            } else {
                if(kind==RowKind.Loading){var spinner=new ProgressBar(owner){IndeterminateTintList=Android.Content.Res.ColorStateList.ValueOf(Ui.Selected)};textPanel.AddView(spinner,new LinearLayout.LayoutParams(D(22),D(22)){Gravity=GravityFlags.CenterHorizontal});title.TextSize=12;title.Gravity=GravityFlags.Center;}
                if(kind==RowKind.About){image=new ImageView(owner);image.SetImageResource(Resource.Drawable.yoru_logo);image.SetScaleType(ImageView.ScaleType.FitCenter);textPanel.AddView(image,new LinearLayout.LayoutParams(-1,D(104)));}
                textPanel.AddView(title);if(kind!=RowKind.Heading)textPanel.AddView(subtitle,new LinearLayout.LayoutParams(-1,-2){TopMargin=D(6)});frame.AddView(textPanel);
            }
        }
        private sealed class PosterImage(Context context):ImageView(context){protected override void OnMeasure(int widthMeasureSpec,int heightMeasureSpec){var width=MeasureSpec.GetSize(widthMeasureSpec);SetMeasuredDimension(width,MobileLayout.PosterHeight(width));}}
        public void Cancel(){loading?.Cancel();loading?.Dispose();loading=null;hero?.Cancel();downloadCard?.Release();}
        public void PrepareDetails(string animeId)=>hero?.PrepareDetails(animeId);
        public void Bind(FeedRow next){
            var artworkChanged=row?.Anime?.Id!=next.Anime?.Id;row=next;title.Text=next.Title;subtitle.Text=next.Subtitle;
            if(kind==RowKind.Poster){
                var compact=owner.CompactExplore;title.TextSize=compact?12:14;subtitle.TextSize=compact?10:12;
                var margins=(RecyclerView.LayoutParams)frame.LayoutParameters!;margins.LeftMargin=margins.RightMargin=D(compact?3:6);frame.LayoutParameters=margins;
                // Fixed two-line title area respects system font scaling and keeps each row aligned.
                title.SetMaxLines(2);var height=(LinearLayout.LayoutParams)title.LayoutParameters!;height.Height=D((compact?38:42)*Math.Clamp(owner.Resources!.Configuration!.FontScale,1,2));title.LayoutParameters=height;
            }
            frame.ContentDescription=next.Title;frame.Clickable=next.Anime is not null || next.Episode is not null || next.Progress is not null || next.Action.Length>0;frame.Focusable=frame.Clickable;
            subtitle.Visibility=next.Subtitle.Length==0?ViewStates.Gone:ViewStates.Visible;
            if(downloadCard is not null && next.Group is { } group){downloadCard.Bind(group,next.Expanded);return;}
            if(kind is RowKind.Hero or RowKind.HeroSkeleton){var p=frame.LayoutParameters!;p.Height=D(owner.HeroHeight);frame.LayoutParameters=p;if(skeletonHeader is not null){var h=(FrameLayout.LayoutParams)skeletonHeader.LayoutParameters!;h.TopMargin=owner.SafeTop;skeletonHeader.LayoutParameters=h;}hero?.Bind(next);return;}
            if(kind==RowKind.Heading){var small=next.Key is "library-heading" or "preferences-heading";title.TextSize=small?11:21;title.LetterSpacing=small?.12f:0;title.SetTextColor(small?Ui.Muted:Ui.Text);}
            if(kind==RowKind.Message && next.Key=="detail-story")subtitle.SetMaxLines(int.MaxValue);else if(kind==RowKind.Message)subtitle.SetMaxLines(3);
            if(optionIcon is not null){var icon=next.Action.StartsWith("navigate:")?next.Action[9..]:next.Action switch{"clear-history"=>"Remove","language"=>"Settings","open-search"=>"Search","genres"=>"Genres","sort"=>"Chart",_=>"More"};optionIcon.SetImageDrawable(new StreamIcon(icon,next.Action=="clear-history"?Color.ParseColor("#E49AAB"):Ui.Selected));
                if(divider is not null)divider.Visibility=next.Key is "more:MyList" or "more:Genres" or "more:Seasonal"?ViewStates.Visible:ViewStates.Gone;
                var background=Ui.Shape(Ui.Panel,D(16));if(next.Key is "more:MyList" or "more:Genres" or "more:Seasonal" or "more:Downloads"){var top=next.Key=="more:MyList"?D(16):D(3);var bottom=next.Key=="more:Downloads"?D(16):D(3);background.SetCornerRadii(new float[]{top,top,top,top,bottom,bottom,bottom,bottom});}frame.Background=background;
            }
            if(save is not null){save.SetImageDrawable(new StreamIcon(next.Saved?"Check":"Add",Ui.Text));save.ContentDescription=L.T(next.Saved?"Saved":"AddList");}
            if(image is not null && next.Anime is not null && (artworkChanged || image.Drawable is null)){Cancel();image.SetImageDrawable(null);loading=new();var width=kind==RowKind.Poster?Math.Clamp(((owner.CompactExplore?owner.CatalogWidthPixels:owner.Resources!.DisplayMetrics!.WidthPixels)-D(32))/owner.Columns,160,640):D(90);_=YoruApp.Images.Load(image,AnimeMetadata.Art(next.Anime,false),width,(int)(width*1.5),loading.Token);}
            if(progress is not null && next.Progress is not null)progress.Progress=next.Progress.Duration>0?(int)Math.Clamp(next.Progress.Seconds/next.Progress.Duration*100,0,100):0;
            if(downloadButton is not null && next.Anime is not null && next.Episode is not null){
                var job=next.Download??YoruApp.Downloads.Find(next.Anime.Id,next.Episode.Id);var state=job is null?"":DownloadText.Status(job);subtitle.Text=kind==RowKind.Download && job is not null?DownloadText.Info(job):string.Join("\n",new[]{next.Subtitle,state}.Where(value=>value.Length>0));subtitle.SetMaxLines(4);subtitle.Visibility=subtitle.Text?.Length>0?ViewStates.Visible:ViewStates.Gone;
                downloadButton.SetImageDrawable(new StreamIcon(kind==RowKind.Download?"More":job?.Status==DownloadStatus.Completed?"Check":job?.Active==true?"History":"Download",Ui.Selected));downloadButton.ContentDescription=kind==RowKind.Download?L.T("DownloadManage"):job is null?L.T("DownloadEpisode"):L.T("DownloadManage");
                downloadProgress!.Visibility=job?.Active==true?ViewStates.Visible:ViewStates.Gone;downloadProgress.Indeterminate=job?.Percent is null;downloadProgress.Progress=job?.Percent??0;
            }
        }
    }

    // Static, cheap placeholders: no independent animators running in every recycled cell.
    private sealed class LoadingSkeleton(MainActivity owner,bool hero,bool episode=false):View(owner)
    {
        private readonly Paint paint=new(PaintFlags.AntiAlias);
        protected override void OnMeasure(int widthMeasureSpec,int heightMeasureSpec){var width=MeasureSpec.GetSize(widthMeasureSpec);SetMeasuredDimension(width,episode?Ui.Dp(owner,72):hero?Ui.Dp(owner,owner.HeroHeight):MobileLayout.PosterHeight(width)+Ui.Dp(owner,82));}
        protected override void OnDraw(Canvas canvas){base.OnDraw(canvas);float D(float n)=>Ui.Dp(owner,n);paint.Color=Color.ParseColor("#181D2D");
            if(episode){canvas.DrawRoundRect(D(16),D(16),D(56),D(56),D(10),D(10),paint);canvas.DrawRoundRect(D(72),D(20),Width-D(28),D(32),D(3),D(3),paint);canvas.DrawRoundRect(D(72),D(42),Width*.65f,D(51),D(3),D(3),paint);}
            else if(hero){canvas.DrawRect(0,0,Width,Height,paint);paint.Color=Color.ParseColor("#252A3C");canvas.DrawRoundRect(D(22),Height-D(160),Width-D(60),Height-D(130),D(6),D(6),paint);canvas.DrawRoundRect(D(22),Height-D(116),Width-D(110),Height-D(102),D(4),D(4),paint);canvas.DrawRoundRect(D(22),Height-D(72),Width/2f-D(5),Height-D(24),D(13),D(13),paint);canvas.DrawRoundRect(Width/2f+D(5),Height-D(72),Width-D(22),Height-D(24),D(13),D(13),paint);}
            else{var bottom=MobileLayout.PosterHeight(Width);canvas.DrawRoundRect(0,0,Width,bottom,D(12),D(12),paint);canvas.DrawRoundRect(0,bottom+D(12),Width-D(14),bottom+D(25),D(3),D(3),paint);canvas.DrawRoundRect(0,bottom+D(34),Width*.65f,bottom+D(44),D(3),D(3),paint);}
        }
    }
}
