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

    [Fact]
    public void Pasta_antiga_vem_inteira_para_a_nova()
    {
        var raiz = Directory.CreateTempSubdirectory("rdtd-").FullName;
        try
        {
            var antiga = Path.Combine(raiz, "Faixa");
            var nova = Path.Combine(raiz, "RD Track Downloader");
            Directory.CreateDirectory(Path.Combine(antiga, "bin"));
            File.WriteAllText(Path.Combine(antiga, "bin", "yt-dlp.exe"), "x");
            File.WriteAllText(Path.Combine(antiga, "config.json"), "{}");

            Componentes.TrazerPasta(antiga, nova);

            Assert.True(File.Exists(Path.Combine(nova, "bin", "yt-dlp.exe")));
            Assert.Equal("{}", File.ReadAllText(Path.Combine(nova, "config.json")));
            Assert.False(Directory.Exists(antiga));
        }
        finally
        {
            Directory.Delete(raiz, true);
        }
    }

    [Fact]
    public void O_que_ja_existe_na_pasta_nova_fica()
    {
        var raiz = Directory.CreateTempSubdirectory("rdtd-").FullName;
        try
        {
            var antiga = Path.Combine(raiz, "Faixa");
            var nova = Path.Combine(raiz, "RD Track Downloader");
            Directory.CreateDirectory(Path.Combine(antiga, "bin"));
            Directory.CreateDirectory(nova);
            File.WriteAllText(Path.Combine(antiga, "config.json"), "antiga");
            File.WriteAllText(Path.Combine(nova, "config.json"), "nova");

            Componentes.TrazerPasta(antiga, nova);

            Assert.Equal("nova", File.ReadAllText(Path.Combine(nova, "config.json")));
            Assert.True(Directory.Exists(Path.Combine(nova, "bin")));
            Assert.True(File.Exists(Path.Combine(antiga, "config.json"))); // não apaga o que não trouxe
        }
        finally
        {
            Directory.Delete(raiz, true);
        }
    }

    [Fact]
    public void Sem_pasta_antiga_nao_faz_nada()
    {
        var raiz = Directory.CreateTempSubdirectory("rdtd-").FullName;
        try
        {
            var nova = Path.Combine(raiz, "RD Track Downloader");
            Componentes.TrazerPasta(Path.Combine(raiz, "Faixa"), nova);
            Assert.False(Directory.Exists(nova));
        }
        finally
        {
            Directory.Delete(raiz, true);
        }
    }

    [Fact]
    public void Pasta_interna_tem_o_nome_do_programa() =>
        Assert.Equal("RD Track Downloader", Path.GetFileName(Componentes.PastaApp));
}
