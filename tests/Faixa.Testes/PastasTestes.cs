namespace Faixa.Testes;

public class PastasTestes
{
    [Fact]
    public void Pasta_padrao_fica_em_Downloads_Musicas() =>
        Assert.Equal("Downloads › Músicas", Config.NomeAmigavel(Config.PastaPadrao));

    [Fact]
    public void Subpasta_de_playlist_mostra_o_caminho_desde_Downloads() =>
        Assert.Equal("Downloads › Músicas › Clássicos dos 80",
            Config.NomeAmigavel(Path.Combine(Config.PastaPadrao, "Clássicos dos 80")));

    [Fact]
    public void Pasta_Musicas_do_Windows_aparece_pelo_nome() =>
        Assert.Equal("Músicas", Config.NomeAmigavel(Config.PastaMusicas));

    [Fact]
    public void Pasta_longe_das_conhecidas_mostra_so_o_nome() =>
        Assert.Equal("Coleção", Config.NomeAmigavel(@"Z:\Arquivo\Muito\Fundo\Coleção"));
}
