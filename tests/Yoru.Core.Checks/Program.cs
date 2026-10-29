using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnimeWitcherDesktop;
using Yoru.Core;
var checks=new List<string>();
void Check(bool value,string message){if(!value)throw new Exception(message);}
Anime MakeAnime(string id)=>AnimeWitcherDesktop.Anime.Parse(new JsonObject{["objectID"]=id,["name"]="Title "+id,["poster_uri"]="https://example.invalid/poster.jpg",["details"]=new JsonObject{["year"]="2026"},["tags"]=new JsonArray("Action")});
var root=Path.Combine(Path.GetTempPath(),"yoru-android-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);AppStorage.Configure(root);
using(var api=new WitcherApi(new FixtureHandler())) {
 await api.Initialize();var page=await api.Search("\u062c\u0648\u062c\u0648\u062a\u0633\u0648 & dragon",2);Check(page.Items.Count==1 && page.Page==2 && page.Pages==3,"Catalog/Arabic search compatibility failed");
 Check((await api.Details("anime-1")).Story=="Synopsis","Details compatibility failed");Check((await api.Episodes("anime-1")).Count==1 && (await api.Episodes("fallback")).Count==2,"Episode summary/fallback failed");
 Check((await api.Servers("anime-1","001")).Count==2,"Servers mapping failed");
 var parsed=(await api.Servers("anime-1","001")).Single(server=>server.Name=="Mirror");Check(parsed.Quality=="720" && parsed.Link=="https://video.example/nested.mp4" && parsed.Direct,"Nested/alternate server response fields were not parsed");
}
checks.Add("Copied backend: dynamic configuration, catalog/search, Unicode queries, details, paginated episodes and servers");
using(var mediaHttp=new HttpClient(new PlaylistFixture())) {var resolver=new PlaybackResolver(mediaHttp,(_,_)=>Task.FromResult<JsonObject?>(null));var result=await resolver.Resolve(new VideoServer("HLS","Auto","https://fixture.invalid/signed-playback?token=test",true,false),CancellationToken.None);Check(result.MimeType=="application/x-mpegURL" && result.Direct && !result.Browser,"Signed HLS MIME type was lost");}
checks.Add("Extensionless signed HLS streams retain their MIME type for Media3 source selection");
await ResolverChecks.Run();checks.Add("Copied resolver: MF/KF/PD/ST, HLS/media signatures, attachment streams, redirects, unavailable/denied/HTML rejection and cancellation");
await StartupCacheChecks.Run();checks.Add("Copied startup cache: cold/warm metadata, atomic/bounded writes, corruption/schema safety and cancellation");
var store=new MobileStore(root);Check(L.Language=="en" && MobileStore.ReadLanguage(root)=="en","First launch is not English");await store.SetLanguage("ar");Check(new MobileStore(root).History.Count==0 && L.Language=="ar" && MobileStore.ReadLanguage(root)=="ar","Arabic preference did not persist");await store.SetLanguage("en");
Check(L.T("MobileServers")!="MobileServers","Mobile translations missing");foreach(var pair in L.Entries){Check(pair.Value.Length==2 && pair.Value.All(value=>value.Length>0),"Missing translation "+pair.Key);}
checks.Add("English defaults, persisted immediate language changes, complete EN/AR translation pairs");
var first=MakeAnime("first");var second=MakeAnime("second");var episode=new Episode("001","Episode 1","",new JsonObject());
await store.ToggleFavorite(first);await store.ToggleFavorite(second);var restarted=new MobileStore(root);Check(restarted.Favorites.Count==2 && restarted.IsFavorite(first.Id),"Favorites did not persist");await restarted.ToggleFavorite(first);Check(new MobileStore(root).Favorites.Select(item=>item.Id).SequenceEqual(new[]{second.Id}),"Favorite removal affected other records");
checks.Add("Favorite add/remove and restart persistence without unrelated changes");
var began=DateTime.UtcNow.AddSeconds(-5);await store.SaveProgress(first,episode,73,120,began);await store.SaveProgress(second,episode,50,100,began);await store.RemoveHistory(first.Id);await store.SaveProgress(first,episode,80,120,began);restarted=new MobileStore(root);Check(restarted.History.Count==1 && restarted.Position(first.Id,episode.Id)==0 && restarted.Position(second.Id,episode.Id)==50,"Individual deletion or stale-player suppression failed");
await restarted.ClearHistory();await restarted.SaveProgress(second,episode,60,100,began);Check(new MobileStore(root).History.Count==0 && new MobileStore(root).Favorites.Count==1,"Bulk deletion lost data isolation or returned stale progress");
await restarted.SaveProgress(first,episode,18,120,DateTime.UtcNow.AddSeconds(1));Check(new MobileStore(root).Position(first.Id,episode.Id)==18,"Fresh explicit playback could not create new progress");
checks.Add("Individual/bulk progress deletion survives restart, ignores stale player saves, preserves favorites and allows new playback");
var calls=0;var response=new TaskCompletionSource<SearchPage>();var session=new CatalogSession("query",(_,_,_)=>{Interlocked.Increment(ref calls);return response.Task;});
var loading=session.Load();Check(!await session.Load() && calls==1,"Concurrent catalog fetch was duplicated");response.SetResult(new SearchPage(new(){first,first,second},0,2,4));await loading;Check(session.Items.Count==2 && session.NextPage==1,"Catalog did not deduplicate");
var batches=new CatalogSession("",(_,page,_)=>Task.FromResult(new SearchPage(page==0?new(){first,second}:new(){second,MakeAnime("third")},page,2,3)));await batches.Load();await batches.Load();Check(batches.Complete && batches.Items.Count==3 && !await batches.Load(),"Infinite scroll completion/deduplication failed");
var retryCalls=0;var retry=new CatalogSession("",(_,p,_)=>++retryCalls==1?Task.FromException<SearchPage>(new HttpRequestException("offline")):Task.FromResult(new SearchPage(new(){first},p,1,1)));Check(!await retry.Load() && retry.NextPage==0,"Failure advanced catalog page");Check(await retry.Load() && retry.Complete,"Failed page could not retry");
checks.Add("Infinite catalog: one request at a time, deduplication, end detection, failure retry without skipping pages");
var shared=new AsyncResultCache<string,int>(2,TimeSpan.FromMinutes(1));var count=0;var tasks=Enumerable.Range(0,30).Select(_=>shared.Get("same",async()=>{Interlocked.Increment(ref count);await Task.Delay(20);return 9;}));Check((await Task.WhenAll(tasks)).All(value=>value==9)&&count==1,"Request coalescing failed");
checks.Add("Bounded shared API requests coalesce concurrent consumers");
foreach(var width in new[]{320,360,412,600,800,1024,1280}){var columns=MobileLayout.Columns(width);var card=(width-16d)/columns-12;Check(columns>=2 && columns<=6 && card>=130 && card<=230,"Phone/tablet poster grid geometry is invalid");Check(width/(double)MobileNavigation.Tabs.Count>=48,"Bottom navigation has undersized touch targets");}
foreach(var (width,height) in new[]{(320,568),(360,640),(412,915),(600,960),(800,1280),(640,360),(1024,600),(1280,800)}) {
 var hero=MobileLayout.HeroHeight(width,height,24);
 Check(hero>=284,"Hero cannot fit safe-area branding and actions");
 if(height>=width && height>=640)Check(hero>=height*.45 && hero<=height*.55,"Portrait hero is outside the cinematic height range");
 var nav=MobileLayout.NavigationWidth(width);Check(nav<=640 && nav<=width-28 && (nav-10)/(double)MobileNavigation.Tabs.Count>=48,"Floating navigation clips or undersizes touch targets");
 foreach(var bottom in new[]{0,16,24,48})Check(MobileLayout.ContentBottomPadding(bottom)>=bottom+10+72+12,"Last feed item can be hidden beneath the floating bar");
 foreach(var scale in new[]{1f,1.3f,1.5f,2f}) {
  Check(MobileLayout.HeroHeight(width,height,24,scale)>=24+70+190*scale-1,"Large type overlaps hero branding/actions");
  Check(MobileLayout.ContentBottomPadding(24,scale)>=24+10+MobileLayout.NavigationHeight(scale)+12,"Scaled navigation hides the last card");
  Check(MobileLayout.NavigationHeight(scale)>=25+3+19*scale,"Scaled navigation label is clipped");
 }
 Check(Math.Abs(MobileLayout.PosterHeight(200)/200d-1.5)<.01,"Poster artwork lost its 2:3 aspect ratio");
}
foreach(var key in new[]{"MobileYourSpace","MobileLibrary","MobilePreferences","MobileHintMyList","MobileHintGenres","MobileHintSeasonal","MobileHintSettings","MobileSearch","MobileSynopsis"})Check(L.Entries[key][1].Any(c=>c>='\u0600' && c<='\u06ff'),"New Arabic translation is invalid: "+key);
checks.Add("Cinematic phone/tablet and landscape geometry: 2:3 posters, 53% portrait hero, 48dp navigation targets, system-inset clearance and 100-200% text scaling");
foreach(var width in new[]{320,360,412,600,800,1280}){
 Check(MobileLayout.ColumnsForPage("Explore",width)==Math.Max(3,(int)Math.Ceiling(width/160d)),"Explore must grow beyond three columns on larger containers");
 foreach(var page in new[]{"Home","MyList","Genres","Seasonal"})Check(MobileLayout.ColumnsForPage(page,width)==MobileLayout.Columns(width),"Other page grid changed");
 Check(width/(double)MobileLayout.ColumnsForPage("Explore",width)<=160 && width/(double)MobileLayout.ColumnsForPage("Explore",width)-14>90,"Explore poster sizing is outside its compact phone / bounded tablet range");
}
Check(EpisodeNavigation.Target(0,3,-1) is null && EpisodeNavigation.Target(2,3,1) is null,"Unavailable episode action enabled");
Check(EpisodeNavigation.Target(1,3,-1)==0 && EpisodeNavigation.Target(1,3,1)==2,"Adjacent episode navigation failed");
Check(EpisodeNavigation.Target(-1,3,1) is null && EpisodeNavigation.Target(0,0,1) is null && EpisodeNavigation.Target(0,1,1) is null,"Missing/single episode navigation failed");
checks.Add("Responsive Explore geometry and previous/next episode boundary safety");
var flat=Enumerable.Repeat(unchecked((int)0xff355577),35).ToArray();Check(BackdropBlur.Apply(flat,7,5).SequenceEqual(flat),"Frost blur changes a flat color");
Check(BackdropBlur.Apply(new[]{unchecked((int)0xffc08040)},1,1)[0]==unchecked((int)0xffc08040),"Frost blur fails on a single pixel");
var impulse=Enumerable.Repeat(unchecked((int)0xff000000),13*9).ToArray();impulse[4*13+6]=unchecked((int)0xffffffff);var blurred=BackdropBlur.Apply(impulse,13,9);
Check(impulse[4*13+6]==unchecked((int)0xffffffff) && blurred[4*13+6]!=impulse[4*13+6] && (blurred[4*13+5]&255)>0,"Frost blur mutates source or does not spread details");
Check(blurred.All(value=>(uint)value>>>24==255),"Frost blur corrupts opacity");Check(BackdropBlur.Apply(flat,7,5,0).SequenceEqual(flat),"Zero-radius blur is invalid");
var rejected=false;try{BackdropBlur.Apply(flat,7,6);}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected,"Frost blur accepts malformed dimensions");
var sample=Enumerable.Range(0,200*44).Select(value=>unchecked((int)0xff000000)|(value*31&0xffffff)).ToArray();var watch=System.Diagnostics.Stopwatch.StartNew();for(var i=0;i<48;i++)BackdropBlur.Apply(sample,200,44);watch.Stop();Console.WriteLine($"PERF: desktop-only 200x44 navigation blur mean {watch.Elapsed.TotalMilliseconds/48:0.00} ms; not an Android FPS measurement");
checks.Add("Downsampled navigation frost: flat colors, opacity, edge clamping, small dimensions, source isolation and invalid-input protection");
await EpisodeLibraryChecks.Run();checks.Add("Episode revisit regression: real API-cache isolation, immutable snapshots, immediate cached reopen, empty/failed refresh retention, cancellation and per-anime bounded cache");
foreach(var child in new[]{"MyList","Genres","Seasonal","Settings"}){
 Check(MobileNavigation.BackTarget(child)=="More","More child Back skips its parent: "+child);
 Check(MobileNavigation.SelectedTab(child)=="More","More child loses selected tab: "+child);
 Check(MobileNavigation.BackTarget("Details",child)==child && MobileNavigation.SelectedTab("Details",child)=="More","Details lost its More-section parent: "+child);
 var parent=MobileNavigation.BackTarget("Details",child)!;Check(MobileNavigation.BackTarget(parent)=="More","Details -> subpage -> More chain is broken");
}
Check(MobileNavigation.BackTarget("Home") is null && MobileNavigation.BackTarget("More")=="Home","Root Back behavior changed");
foreach(var tab in new[]{"Explore","History"})Check(MobileNavigation.BackTarget(tab)=="Home" && MobileNavigation.SelectedTab(tab)==tab,"Other tab navigation changed: "+tab);
Check(MobileNavigation.Tabs.SequenceEqual(new[]{"Home","Explore","History","More"}) && MobileNavigation.NormalizePage("Trending")=="Explore" && MobileNavigation.SelectedTab("Details","Trending")=="Explore","Removed Trending remains reachable or saved state is not migrated");
checks.Add("More navigation hierarchy: all four subpages return to More, keep More selected, survive restored page names and preserve details/root/tab Back behavior");
await ViewportBookmarkChecks.Run();checks.Add("Catalog viewport bookmarks: exact signed pixel offsets, stable item anchors, query/sort isolation, bounded retention, removed-row clamping and reuse of loaded multi-page sessions");
var artworkPairs=new[]{("One Piece","Featured/Onepiece.jpg"),("Ore dake Level Up na Ken","Featured/levelup.jpg"),("Solo Leveling","Featured/levelup.jpg"),("Jujutsu Kaisen 2nd Season","Featured/JujutsuKaisen S2.jpg"),("Black Clover","Featured/BlackClover.jpg"),("Jujutsu Kaisen","Featured/jujutsukaisen S1.jpg")};
foreach(var (title,asset) in artworkPairs){Check(FeaturedArtwork.ForPage("Home",title)==asset,"Featured image mapping failed: "+title);foreach(var other in new[]{"Explore","Details","History","MyList","Seasonal","Genres"})Check(FeaturedArtwork.ForPage(other,title) is null,"Home artwork leaked into "+other);}
Check(FeaturedArtwork.ForPage("Home","  ONE-PIECE  ")=="Featured/Onepiece.jpg" && FeaturedArtwork.ForPage("Home","Jujutsu Kaisen (TV)")=="Featured/jujutsukaisen S1.jpg","Title punctuation/case aliases failed");
foreach(var title in new[]{"One Piece Film Red","Jujutsu Kaisen 0","Jujutsu Kaisen 3rd Season","Black Clover: Sword of the Wizard King","Ore dake Level Up na Ken 2nd Season"})Check(FeaturedArtwork.ForPage("Home",title) is null,"Different movie/season received wrong custom artwork: "+title);
checks.Add("Featured custom artwork: all five source files matched by title, season 1/2 distinction, aliases, non-Home isolation and movie/other-season rejection");
await DownloadChecks.Run();checks.Add("Episode downloads: real server/quality selection, direct and HLS offline archives, sizes/progress, restart recovery, duplicates, retry/cancel/delete, storage/expiry/DRM rejection and Arabic/More navigation");
await ServerValidationChecks.Run();checks.Add("Server preflight: playable/downloadable fallback, grouped actual qualities, short-lived reuse, bounded probes, retries/cancellation and honest network/source errors");
OfflineChecks.Run();checks.Add("Offline mode: launch/downloads redirect, online-only page and tab gating, local-page preservation, debounced loss, immediate reconnection, single-shot notices and Arabic copy");
DownloadGroupChecks.Run();checks.Add("Responsive Explore phone/foldable/tablet grids and grouped download counts, sizes, active percentages, incomplete states and stable episode order");
if(args.Contains("--live")){
 using var api=new WitcherApi(new NodeHttpHandler());await api.Initialize();var page=await api.Search("",0);Check(page.Items.Count>0,"Live catalog is empty");var query=await api.Search("Jujutsu Kaisen",0);var candidate=query.Items.FirstOrDefault()??page.Items[0];var details=await api.Details(candidate.Id);var episodes=await api.Episodes(candidate.Id);Check(episodes.Count>0,"Live episodes missing");var servers=await api.Servers(candidate.Id,episodes[0].Id);Check(servers.Count>0,"Live server list missing");
 var resolvedCount=0;foreach(var server in servers.Where(item=>item.Name is "MF" or "PD").GroupBy(item=>item.Name).Select(group=>group.First()).Take(2)){try{var media=await api.Resolve(server);Check(media.Direct && !media.Browser,"Live resolver returned a browser page");resolvedCount++;Console.WriteLine("LIVE: "+server.Name+" resolved to validated media; MIME="+media.MimeType);}catch(PlaybackException){Console.WriteLine("LIVE: "+server.Name+" unavailable; handled by resolver");}}
 Check(resolvedCount>0,"No tested live server resolved to media");
 Console.WriteLine($"LIVE: {page.Items.Count} catalog rows, {page.Total} titles, {episodes.Count} episodes, {servers.Count} servers; no credentials/stream URLs logged");checks.Add("Live backend catalog, search, details, episodes and server list");
}
foreach(var check in checks)Console.WriteLine("PASS: "+check);
Directory.CreateDirectory("evidence");await File.WriteAllTextAsync("evidence/core-checks.json",JsonSerializer.Serialize(new{success=true,checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS: "+checks.Count+" Android core groups");

sealed class PlaylistFixture:HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){var response=new HttpResponseMessage(System.Net.HttpStatusCode.OK){RequestMessage=request,Content=new StringContent("#EXTM3U\n#EXT-X-TARGETDURATION:8\nhttps://fixture.invalid/part.ts\n")};response.Content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.apple.mpegurl");return Task.FromResult(response);}}
