namespace Yoru;
public static class AppStorage {
    private static string? directory;
    public static string DirectoryPath=>directory??throw new InvalidOperationException("Android storage has not been initialized");
    public static void Configure(string path){Directory.CreateDirectory(path);directory=path;}
}
