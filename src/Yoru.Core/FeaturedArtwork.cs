namespace Yoru.Core;

public static class FeaturedArtwork
{
    public static string? ForPage(string page,string animeTitle)
    {
        if(page!="Home")return null;
        var name=new string(animeTitle.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return name switch
        {
            "onepiece" or "onepiecetv"=>"Featured/Onepiece.jpg",
            "oredakelevelupnaken" or "sololeveling" or "oredakelevelupnakensololeveling"=>"Featured/levelup.jpg",
            "jujutsukaisen2ndseason" or "jujutsukaisentv2ndseason" or "jujutsukaisenseason2" or "jujutsukaisens2"=>"Featured/JujutsuKaisen S2.jpg",
            "blackclover" or "blackclovertv"=>"Featured/BlackClover.jpg",
            "jujutsukaisen" or "jujutsukaisentv" or "jujutsukaisen1stseason" or "jujutsukaisenseason1" or "jujutsukaisens1"=>"Featured/jujutsukaisen S1.jpg",
            _=>null
        };
    }
}
