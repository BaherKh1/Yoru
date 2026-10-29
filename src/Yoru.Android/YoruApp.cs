using Android.App;
using Android.Content;
using Android.Runtime;
using Yoru.Core;
namespace Yoru.AndroidApp;
[Application(Label="YORU",AllowBackup=false)]
public sealed class YoruApp(IntPtr handle,JniHandleOwnership ownership) : Application(handle,ownership)
{
    public static AppState State{get;private set;}=null!;
    public static PosterCache Images{get;private set;}=null!;
    public static DownloadLibrary Downloads{get;private set;}=null!;
    public override void OnCreate(){base.OnCreate();AndroidX.AppCompat.App.AppCompatDelegate.DefaultNightMode=AndroidX.AppCompat.App.AppCompatDelegate.ModeNightYes;State=new AppState(Path.Combine(FilesDir!.AbsolutePath,"yoru"));Images=new PosterCache(Path.Combine(CacheDir!.AbsolutePath,"posters"));Downloads=new DownloadLibrary(Path.Combine(FilesDir.AbsolutePath,"episode-downloads"));}
    public override void OnTrimMemory(TrimMemory level){base.OnTrimMemory(level);if(level>=TrimMemory.RunningLow)Images.ClearMemory();}
}
public abstract class LocalizedActivity : AndroidX.AppCompat.App.AppCompatActivity
{
    protected override void AttachBaseContext(Context? context){
        if(context is null){base.AttachBaseContext(context);return;}
        var language=MobileStore.ReadLanguage(Path.Combine(context.FilesDir!.AbsolutePath,"yoru"));
        var configuration=new Android.Content.Res.Configuration(context.Resources!.Configuration);
        var locale=Java.Util.Locale.ForLanguageTag(language)!;configuration.SetLocale(locale);configuration.SetLayoutDirection(locale);
        base.AttachBaseContext(context.CreateConfigurationContext(configuration));
    }
}
