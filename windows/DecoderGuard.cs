using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AnimeWitcherDesktop;

// Keep native decoder crashes isolated. A crashed player leaves its marker; the
// next player uses full-quality software decoding for a day instead of crashing
// repeatedly on an incompatible GPU/driver. Normal closes remove the marker.
internal sealed class DecoderGuard : IDisposable
{
    public bool SoftwareRequired { get; }
    private readonly string marker;
    public DecoderGuard(string? storageDirectory=null)
    {
        var directory=storageDirectory ?? Path.Combine(AppStorage.DirectoryPath,"decoder-safety");
        marker=Path.Combine(directory,"player-"+Environment.ProcessId+".json");
        try {
            Directory.CreateDirectory(directory); var compatibility=Path.Combine(directory,"compatibility.json");
            if(File.Exists(compatibility) && DateTime.TryParse(File.ReadAllText(compatibility),null,System.Globalization.DateTimeStyles.RoundtripKind,out var until)) SoftwareRequired=until>DateTime.UtcNow;
            foreach(var path in Directory.EnumerateFiles(directory,"player-*.json")) {
                var record=JsonSerializer.Deserialize<Marker>(File.ReadAllText(path)); if(record is null) continue;
                var alive=false;
                try { using var process=Process.GetProcessById(record.ProcessId); alive=!process.HasExited && Math.Abs((process.StartTime.ToUniversalTime()-record.StartedUtc).TotalSeconds)<2; } catch(ArgumentException) { } catch(InvalidOperationException) { }
                if(alive) continue;
                if(record.Mode=="automatic" && record.CreatedUtc>DateTime.UtcNow.AddDays(-1)) { SoftwareRequired=true; File.WriteAllText(compatibility,DateTime.UtcNow.AddDays(1).ToString("O")); }
                File.Delete(path);
            }
            using var current=Process.GetCurrentProcess();
            File.WriteAllText(marker,JsonSerializer.Serialize(new Marker(Environment.ProcessId,current.StartTime.ToUniversalTime(),DateTime.UtcNow,SoftwareRequired?"software":"automatic")));
        } catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception) { }
    }
    private sealed record Marker(int ProcessId,DateTime StartedUtc,DateTime CreatedUtc,string Mode);
    public void Dispose() { try { if(File.Exists(marker)) File.Delete(marker); } catch(IOException) { } catch(UnauthorizedAccessException) { } }
}
