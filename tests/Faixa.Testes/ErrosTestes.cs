namespace Faixa.Testes;

public class ErrosTestes
{
    [Theory]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you're not a bot", TipoErro.Bloqueio)]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm your age. This video may be inappropriate", TipoErro.Restrito)]
    [InlineData("ERROR: [youtube] abc: Join this channel to get access to members-only content", TipoErro.Restrito)]
    [InlineData("ERROR: [youtube] abc: Private video. Sign in if you've been granted access", TipoErro.Indisponivel)]
    [InlineData("ERROR: [youtube] abc: Video unavailable", TipoErro.Indisponivel)]
    [InlineData("ERROR: Unable to download webpage: <urlopen error [Errno 11001] getaddrinfo failed>", TipoErro.Conexao)]
    [InlineData("ERROR: Postprocessing: Conversion failed!", TipoErro.Conversao)]
    [InlineData("ERROR: [Errno 13] Permission denied: 'C:\\x.mp3'", TipoErro.Gravacao)]
    [InlineData("ERROR: algo que ninguém previu", TipoErro.Desconhecido)]
    public void Traduz_o_erro_do_yt_dlp(string linha, TipoErro tipo) =>
        Assert.Equal(tipo, Baixador.Traduzir(["[youtube] Extracting URL", linha]).Tipo);

    [Fact]
    public void Usa_a_ultima_linha_de_erro_e_guarda_o_detalhe()
    {
        var erro = Baixador.Traduzir(["WARNING: algo", "ERROR: Video unavailable"]);
        Assert.Equal(TipoErro.Indisponivel, erro.Tipo);
        Assert.Contains("WARNING: algo", erro.Detalhe);
    }
}
