namespace Faixa.Testes;

public class AtualizadorTestes
{
    // A partir da 1.2 a release leva o .exe com o nome novo e uma cópia como Faixa.exe para quem está na 1.1.
    private const string Release = """
        {"tag_name": "v1.2.0", "name": "RD Track Downloader 1.2", "body": "- Playlists\n- Nomes limpos\n",
         "assets": [
          {"name": "Faixa.exe", "browser_download_url": "https://github.com/x/faixa/releases/download/v1.2.0/Faixa.exe", "size": 61871595,
           "digest": "sha256:1111111111111111111111111111111111111111111111111111111111111111"},
          {"name": "RDTrackDownloader.zip", "browser_download_url": "https://github.com/x/faixa/releases/download/v1.2.0/RDTrackDownloader.zip", "size": 56000000},
          {"name": "RDTrackDownloader.exe", "browser_download_url": "https://github.com/x/faixa/releases/download/v1.2.0/RDTrackDownloader.exe", "size": 61871595,
           "digest": "sha256:ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789"}
        ]}
        """;

    // Release da 1.1, só com o nome antigo.
    private const string ReleaseAntiga = """
        {"tag_name": "v1.1.0", "body": "",
         "assets": [
          {"name": "Faixa.exe", "browser_download_url": "https://github.com/x/faixa/releases/download/v1.1.0/Faixa.exe", "size": 61000000}
        ]}
        """;

    [Theory]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("v2", 2, 0, 0)]
    [InlineData("v1.3.1-beta", 1, 3, 1)]
    public void Le_a_versao_da_tag(string tag, int maior, int menor, int correcao) =>
        Assert.Equal(new Version(maior, menor, correcao), Atualizador.LerVersao(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nightly")]
    public void Tag_que_nao_e_versao_e_ignorada(string? tag) => Assert.Null(Atualizador.LerVersao(tag));

    [Fact]
    public void Le_a_release_com_o_exe_e_o_sha256()
    {
        var nova = Atualizador.Interpretar(Release)!;

        Assert.Equal(new Version(1, 2, 0), nova.Versao);
        Assert.Equal("1.2", nova.Texto);
        Assert.EndsWith("/RDTrackDownloader.exe", nova.Download);
        Assert.Equal(61871595, nova.Tamanho);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", nova.Sha256);
        Assert.Contains("Playlists", nova.Notas);
    }

    [Fact]
    public void Release_so_com_o_nome_antigo_ainda_vale()
    {
        var nova = Atualizador.Interpretar(ReleaseAntiga)!;

        Assert.Equal(new Version(1, 1, 0), nova.Versao);
        Assert.EndsWith("/Faixa.exe", nova.Download);
        Assert.Null(nova.Sha256);
    }

    [Fact]
    public void Release_sem_o_exe_nao_conta() =>
        Assert.Null(Atualizador.Interpretar("""{"tag_name": "v9.0.0", "assets": [{"name": "outro.zip"}]}"""));

    [Fact]
    public void Versao_atual_vem_do_Directory_Build_props() =>
        Assert.True(Atualizador.VersaoAtual >= new Version(1, 1, 0));

    [Fact]
    public void Versao_igual_nao_e_nova()
    {
        // 1.1.0.0 (assembly) e 1.1.0 (tag) são a mesma versão depois de normalizar.
        Assert.Equal(Atualizador.VersaoAtual, Atualizador.LerVersao("v" + Atualizador.VersaoAtualTexto));
    }
}
