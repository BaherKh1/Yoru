using Path = System.IO.Path;
using System.Security.Cryptography;
using System.Text;
using Android.Graphics;
using Android.Widget;
namespace Yoru.AndroidApp;
public sealed class PosterCache
{
    private sealed record Cached(Bitmap Image,long Bytes,long Stamp);
    private readonly Dictionary<string,Cached> memory=new();private readonly object gate=new();private readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(15)};private readonly SemaphoreSlim slots=new(3);private readonly string directory;private long bytes,stamp;private int downloads;
    public PosterCache(string directory){this.directory=directory;Directory.CreateDirectory(directory);}
    public void ClearMemory(){lock(gate){memory.Clear();bytes=0;}}
    public async Task Load(ImageView image,string[] urls,int width,int height,CancellationToken ct,Action? onLoaded=null)
    {
        try {var bitmap=await Get(urls,width,height,ct).ConfigureAwait(false);if(bitmap is not null && !ct.IsCancellationRequested)image.Post(()=>{if(!ct.IsCancellationRequested && image.Handle!=IntPtr.Zero){image.SetImageBitmap(bitmap);onLoaded?.Invoke();}});}catch(OperationCanceledException){}catch(Exception){}
    }
    // Packaged Home artwork only; network poster URLs and disk cache stay intact.
    public async Task<bool> LoadAsset(ImageView image,Android.Content.Res.AssetManager assets,string path,int width,int height,CancellationToken ct,Action onLoaded)
    {
        try{
            var key="asset:"+path+"|"+width+"x"+height;Bitmap? bitmap;
            lock(gate){bitmap=memory.TryGetValue(key,out var cached)?cached.Image:null;if(cached is not null)memory[key]=cached with{Stamp=++stamp};}
            if(bitmap is null){
                await slots.WaitAsync(ct).ConfigureAwait(false);
                try{
                    lock(gate){bitmap=memory.TryGetValue(key,out var cached)?cached.Image:null;}
                    if(bitmap is null){
                        bitmap=await Task.Run(()=>{
                            ct.ThrowIfCancellationRequested();
                            using var bounds=new BitmapFactory.Options{InJustDecodeBounds=true};
                            using(var input=assets.Open(path))BitmapFactory.DecodeStream(input,null,bounds);
                            var sample=1;while(bounds.OutWidth/(sample*2)>=width && bounds.OutHeight/(sample*2)>=height)sample*=2;
                            ct.ThrowIfCancellationRequested();
                            using var options=new BitmapFactory.Options{InSampleSize=sample,InScaled=false,InPreferredConfig=Bitmap.Config.Argb8888};
                            using var original=assets.Open(path);return BitmapFactory.DecodeStream(original,null,options);
                        },ct).ConfigureAwait(false);
                        if(bitmap is null)return false;
                        if(ct.IsCancellationRequested){bitmap.Dispose();ct.ThrowIfCancellationRequested();}
                        lock(gate){var entry=new Cached(bitmap,bitmap.AllocationByteCount,++stamp);if(memory.Remove(key,out var old))bytes-=old.Bytes;memory[key]=entry;bytes+=entry.Bytes;while(bytes>24*1024*1024 && memory.Count>1){var oldest=memory.MinBy(pair=>pair.Value.Stamp);memory.Remove(oldest.Key);bytes-=oldest.Value.Bytes;}}
                    }
                }finally{slots.Release();}
            }
            ct.ThrowIfCancellationRequested();var ready=bitmap;
            image.Post(()=>{if(!ct.IsCancellationRequested && image.Handle!=IntPtr.Zero){image.SetImageBitmap(ready);onLoaded();}});return true;
        }catch(OperationCanceledException){return false;}catch(Exception){return false;}
    }
    private async Task<Bitmap?> Get(string[] urls,int width,int height,CancellationToken ct)
    {
        foreach(var url in urls.Take(4)) {
            var key=url+"|"+width+"x"+height;lock(gate){if(memory.TryGetValue(key,out var cached)){memory[key]=cached with{Stamp=++stamp};return cached.Image;}}
            await slots.WaitAsync(ct).ConfigureAwait(false);
            try {
                ct.ThrowIfCancellationRequested();lock(gate){if(memory.TryGetValue(key,out var already)){memory[key]=already with{Stamp=++stamp};return already.Image;}}var file=Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".img");byte[] encoded;
                if(File.Exists(file) && File.GetLastWriteTimeUtc(file)>DateTime.UtcNow.AddDays(-14))encoded=await File.ReadAllBytesAsync(file,ct).ConfigureAwait(false);
                else {
                    using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.UserAgent.ParseAdd("YORU-Android/9.3");
                    using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);response.EnsureSuccessStatusCode();
                    if(response.Content.Headers.ContentLength>8*1024*1024)continue;
                    using var body=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);using var output=new MemoryStream();var buffer=new byte[16384];int count;
                    while((count=await body.ReadAsync(buffer,ct).ConfigureAwait(false))>0){if(output.Length+count>8*1024*1024)throw new IOException("Image is too large");output.Write(buffer,0,count);}encoded=output.ToArray();
                    var temporary=file+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllBytesAsync(temporary,encoded,ct).ConfigureAwait(false);File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
                    if(Interlocked.Increment(ref downloads)%20==0)_=Task.Run(TrimDisk);
                }
                var bitmap=await Task.Run(()=>{
                    using var bounds=new BitmapFactory.Options{InJustDecodeBounds=true};BitmapFactory.DecodeByteArray(encoded,0,encoded.Length,bounds);var sample=1;
                    while(bounds.OutWidth/(sample*2)>=width && bounds.OutHeight/(sample*2)>=height)sample*=2;
                    using var options=new BitmapFactory.Options{InSampleSize=sample,InPreferredConfig=Bitmap.Config.Argb8888};return BitmapFactory.DecodeByteArray(encoded,0,encoded.Length,options);
                },ct).ConfigureAwait(false);
                if(bitmap is null){File.Delete(file);continue;}
                lock(gate){var entry=new Cached(bitmap,bitmap.AllocationByteCount,++stamp);if(memory.Remove(key,out var old))bytes-=old.Bytes;memory[key]=entry;bytes+=entry.Bytes;while(bytes>24*1024*1024 && memory.Count>1){var oldest=memory.MinBy(pair=>pair.Value.Stamp);memory.Remove(oldest.Key);bytes-=oldest.Value.Bytes;}}
                return bitmap;
            }catch(OperationCanceledException){throw;}catch(Exception){}finally{slots.Release();}
        }
        return null;
    }
    private void TrimDisk(){try{var files=new DirectoryInfo(directory).GetFiles("*.img").OrderBy(file=>file.LastWriteTimeUtc).ToArray();var total=files.Sum(file=>file.Length);foreach(var file in files){if(total<=128*1024*1024)break;total-=file.Length;file.Delete();}}catch(IOException){}catch(UnauthorizedAccessException){}}
}
