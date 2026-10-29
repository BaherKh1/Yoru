using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace Yoru;

// Disk originals, display-sized frozen bitmaps, and cancellable queued decoding.
public sealed class ArtworkCache : IDisposable
{
    public static ArtworkCache Shared { get; } = new();
    private readonly HttpClient http;
    private readonly string directory;
    private readonly SemaphoreSlim workers = new(4);
    private readonly ConcurrentDictionary<string,Lazy<Task<byte[]>>> downloads = new();
    private readonly ConcurrentDictionary<string,Lazy<Task<BitmapSource?>>> decoding=new();
    private readonly Dictionary<string,Entry> memory = new();
    private readonly Dictionary<string,DateTime> failed = new();
    private readonly object sync = new();
    private readonly long budget;
    private long bytes, stamp, requests, decodes, hits;
    private DateTime nextTrim;
    private string lastFailure="";
    private sealed record Entry(BitmapSource Image, long Bytes, long Stamp,int Width,int Height);
    public ArtworkCache(string? directory = null, HttpMessageHandler? handler = null, long memoryBudget = 80L*1024*1024)
    {
        this.directory = directory ?? Path.Combine(AppStorage.DirectoryPath,"artwork-v6");
        budget = memoryBudget;
        http = handler is null ? new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer=4, PooledConnectionLifetime=TimeSpan.FromMinutes(5) }) : new HttpClient(handler);
        http.Timeout=TimeSpan.FromSeconds(20); http.DefaultRequestHeaders.UserAgent.ParseAdd("YORU/0.6");
    }
    public object Snapshot() { lock(sync) return new { decodedBytes=bytes, decodedEntries=memory.Count, failures=failed.Count, lastFailure, downloads=Interlocked.Read(ref requests), decodes=Interlocked.Read(ref decodes), hits=Interlocked.Read(ref hits) }; }
    public bool TryReady(string location,int width,int height,out BitmapSource? image)
    {
        width=Math.Clamp((width+63)/64*64,64,3840);height=Math.Clamp((height+63)/64*64,64,3840);
        var key=location+"|"+width+"x"+height;
        lock(sync) {
            if(memory.TryGetValue(key,out var exact)) {memory[key]=exact with {Stamp=++stamp};image=exact.Image;Interlocked.Increment(ref hits);return true;}
            var prefix=location+"|";string? bestKey=null;Entry? best=null;
            foreach(var pair in memory) if(pair.Key.StartsWith(prefix,StringComparison.Ordinal) && ((pair.Value.Width>=width && pair.Value.Height>=height) || (pair.Value.Image.PixelWidth>=width && pair.Value.Image.PixelHeight>=height) || pair.Value.Image.PixelWidth<pair.Value.Width) && (best is null || pair.Value.Bytes<best.Bytes)) {bestKey=pair.Key;best=pair.Value;}
            if(best is not null) {memory[bestKey!]=best with {Stamp=++stamp};image=best.Image;Interlocked.Increment(ref hits);return true;}
        }
        image=null;return false;
    }
    // Warm only existing disk artwork. This method cannot make an HTTP request.
    public async Task<bool> PrimeDisk(string location,int width,int height,CancellationToken ct=default)
    {
        if(string.IsNullOrEmpty(location)) return false;
        width=Math.Clamp((width+63)/64*64,64,3840);height=Math.Clamp((height+63)/64*64,64,3840);
        if(TryReady(location,width,height,out _)) return true;
        var file=Data.WebUrl(location)?Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location))).ToLowerInvariant()+".img"):location;
        if(!File.Exists(file) || Data.WebUrl(location) && File.GetLastWriteTimeUtc(file)<=DateTime.UtcNow.AddDays(-14)) return false;
        await workers.WaitAsync(ct).ConfigureAwait(false);
        try {
            if(TryReady(location,width,height,out _)) return true;
            if(new FileInfo(file).Length>20*1024*1024) return false;
            var original=await File.ReadAllBytesAsync(file,ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
            var bitmap=await Task.Run(()=>Decode(original,width,height),ct).ConfigureAwait(false);Remember(location,width,height,bitmap);return true;
        } catch(OperationCanceledException) {throw;}
        catch(Exception error) when(error is IOException or FileFormatException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) {return false;}
        finally {workers.Release();}
    }
    private void Remember(string location,int width,int height,BitmapSource bitmap)
    {
        var key=location+"|"+width+"x"+height;var size=(long)bitmap.PixelWidth*bitmap.PixelHeight*4;
        lock(sync) {
            if(memory.TryGetValue(key,out var previous)) bytes-=previous.Bytes;
            memory[key]=new(bitmap,size,++stamp,width,height);bytes+=size;
            while((bytes>budget || memory.Count>192) && memory.Count>0) {var oldest=memory.MinBy(pair=>pair.Value.Stamp);bytes-=oldest.Value.Bytes;memory.Remove(oldest.Key);}
        }
    }
    public async Task<BitmapSource?> Get(string location,int width,int height,CancellationToken ct=default)
    {
        width=Math.Clamp((width+63)/64*64,64,3840); height=Math.Clamp((height+63)/64*64,64,3840);
        var key=location+"|"+width+"x"+height;
        lock(sync) if(memory.TryGetValue(key,out var ready)) { memory[key]=ready with { Stamp=++stamp }; Interlocked.Increment(ref hits); return ready.Image; }
        while(true) {
            ct.ThrowIfCancellationRequested();
            var pending=decoding.GetOrAdd(key,_=>new Lazy<Task<BitmapSource?>>(()=>Task.Run(()=>GetCore(location,width,height,ct))));
            var task=pending.Value;
            _=task.ContinueWith(_=>decoding.TryRemove(new KeyValuePair<string,Lazy<Task<BitmapSource?>>>(key,pending)),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            try { return await task.WaitAsync(ct).ConfigureAwait(false); }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested) { /* Another recycled consumer cancelled the shared work. */ }
        }
    }
    private async Task<BitmapSource?> GetCore(string location, int width, int height, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        width=Math.Clamp((width+63)/64*64,64,3840); height=Math.Clamp((height+63)/64*64,64,3840);
        var key=location+"|"+width+"x"+height;
        lock(sync) {
            if(memory.TryGetValue(key,out var cached)) { memory[key]=cached with { Stamp=++stamp }; Interlocked.Increment(ref hits); return cached.Image; }
            if(failed.TryGetValue(location,out var retry) && retry>DateTime.UtcNow) return null;
        }
        await workers.WaitAsync(ct).ConfigureAwait(false);
        try {
            ct.ThrowIfCancellationRequested();
            lock(sync) if(memory.TryGetValue(key,out var cached)) { memory[key]=cached with { Stamp=++stamp }; Interlocked.Increment(ref hits); return cached.Image; }
            byte[] original;
            if(Data.WebUrl(location)) {
                var pending=downloads.GetOrAdd(location,_=>new Lazy<Task<byte[]>>(()=>Download(location,ct)));
                var task=pending.Value;
                _=task.ContinueWith(_=>downloads.TryRemove(new KeyValuePair<string,Lazy<Task<byte[]>>>(location,pending)),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                original=await task.WaitAsync(ct).ConfigureAwait(false);
            } else original=await File.ReadAllBytesAsync(location,ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var bitmap=await Task.Run(() => Decode(original,width,height),ct).ConfigureAwait(false);
            Remember(location,width,height,bitmap);
            return bitmap;
        }
        catch(OperationCanceledException) { throw; }
        catch(Exception error) when(error is IOException or HttpRequestException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException or TaskCanceledException or FileFormatException or UnauthorizedAccessException) {
            if(Data.WebUrl(location) && error is IOException or FileFormatException or NotSupportedException or ArgumentException) {
                var cachedFile=Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location))).ToLowerInvariant()+".img");
                try { if(File.Exists(cachedFile)) File.Delete(cachedFile); } catch(IOException) { } catch(UnauthorizedAccessException) { }
            }
            lock(sync) { lastFailure=error.ToString(); if(failed.Count>512) failed.Clear(); failed[location]=DateTime.UtcNow.AddMinutes(1); } return null;
        }
        finally { workers.Release(); }
    }
    private BitmapSource Decode(byte[] original,int width,int height)
    {
        using var stream=new MemoryStream(original,false);
        var header=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnDemand).Frames[0];
        ValidateDimensions(header);
        var target=Math.Min(header.PixelWidth,(int)Math.Ceiling(Math.Max(width,height*(double)header.PixelWidth/Math.Max(1,header.PixelHeight))));
        stream.Position=0; var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.CreateOptions=BitmapCreateOptions.None; bitmap.DecodePixelWidth=Math.Max(1,target); bitmap.StreamSource=stream; bitmap.EndInit();
        // Detach pixel storage: BitmapImage.StreamSource otherwise retains the
        // complete compressed original outside the decoded-cache byte budget.
        var detached=new WriteableBitmap(bitmap); detached.Freeze();
        Interlocked.Increment(ref decodes); return detached;
    }
    private async Task<byte[]> Download(string url,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try { return await DownloadCore(url,timeout.Token).ConfigureAwait(false); }
        catch(OperationCanceledException error) when(!ct.IsCancellationRequested) { throw new HttpRequestException("Artwork download timed out",error); }
    }
    private async Task<byte[]> DownloadCore(string url,CancellationToken ct)
    {
        var name=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant()+".img";
        var file=Path.Combine(directory,name);
        try { if(File.Exists(file) && File.GetLastWriteTimeUtc(file)>DateTime.UtcNow.AddDays(-14)) {
            var cached=await File.ReadAllBytesAsync(file,ct).ConfigureAwait(false);
            try { ValidatePayload(cached); return cached; }
            catch(Exception error) when(IsImageError(error)) { File.Delete(file); }
        } }
        catch(IOException) { }
        Interlocked.Increment(ref requests);
        HttpResponseMessage received;
        try { received=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false); }
        catch(OperationCanceledException error) when(!ct.IsCancellationRequested) { throw new HttpRequestException("Artwork request timed out",error); }
        using var response=received; response.EnsureSuccessStatusCode();
        var mime=response.Content.Headers.ContentType?.MediaType;
        if(mime is not null && (mime.StartsWith("text/",StringComparison.OrdinalIgnoreCase) || mime.Contains("json",StringComparison.OrdinalIgnoreCase))) throw new IOException("Artwork endpoint returned a document instead of an image");
        if(response.Content.Headers.ContentLength>20*1024*1024) throw new IOException("Artwork is too large");
        await using var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false); using var data=new MemoryStream();
        var chunk=new byte[32768]; int read;
        while((read=await input.ReadAsync(chunk,ct).ConfigureAwait(false))>0) { if(data.Length+read>20*1024*1024) throw new IOException("Artwork is too large"); await data.WriteAsync(chunk.AsMemory(0,read)).ConfigureAwait(false); }
        var result=data.ToArray();
        // Never persist HTML error pages or undecodable bytes as a successful image.
        ValidatePayload(result);
        try {
            Directory.CreateDirectory(directory); var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { await File.WriteAllBytesAsync(temp,result).ConfigureAwait(false); File.Move(temp,file,true); } finally { if(File.Exists(temp)) File.Delete(temp); }
            if(DateTime.UtcNow>nextTrim) { nextTrim=DateTime.UtcNow.AddMinutes(10); TrimDisk(); }
        } catch(Exception error) when(error is IOException or UnauthorizedAccessException) { }
        return result;
    }
    private static bool IsImageError(Exception error) => error is IOException or FileFormatException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException;
    private static void ValidateDimensions(BitmapSource image) {
        if(image.PixelWidth<1 || image.PixelHeight<1 || image.PixelWidth>32768 || image.PixelHeight>32768 || (long)image.PixelWidth*image.PixelHeight>80_000_000) throw new IOException("Unsupported artwork dimensions");
    }
    private static void ValidatePayload(byte[] bytes) {
        using var stream=new MemoryStream(bytes,false);
        ValidateDimensions(BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnDemand).Frames[0]);
    }
    private void TrimDisk()
    {
        var files=new DirectoryInfo(directory).GetFiles("*.img").OrderByDescending(file=>file.LastWriteTimeUtc).ToList(); long total=0;
        foreach(var file in files) { total+=file.Length; if(total>256L*1024*1024 || file.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-14)) { try { file.Delete(); } catch(IOException) { } } }
    }
    public void Dispose() { http.Dispose(); workers.Dispose(); }
}
