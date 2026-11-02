namespace Faixa.Testes;

public class FormatosTestes
{
    private static readonly InfoAudio Audio = Analisador.Interpretar(ExemplosYtDlp.Clipe)!;

    [Fact]
    public void Sem_analise_oferece_so_os_quatro_MP3() =>
        Assert.Equal(["mp3-320", "mp3-256", "mp3-192", "mp3-128"], Formatos.Montar(null).Select(o => o.Chave));

    [Fact]
    public void Com_analise_acrescenta_os_originais()
    {
        var chaves = Formatos.Montar(Audio).Select(o => o.Chave).ToList();

        Assert.Contains("original-opus", chaves);
        Assert.Contains("original-aac", chaves);
    }

    [Fact]
    public void Estima_o_tamanho_do_MP3_pela_duracao()
    {
        // 213 s × 320 kbps = 8,52 MB + 130 KB de capa ≈ 8,2 MiB.
        var mp3 = Formatos.Montar(Audio).First(o => o.Chave == "mp3-320");
        Assert.Equal("≈ 8,2 MB", mp3.Tamanho);
    }

    [Theory]
    [InlineData("original-opus", "original-opus")]
    [InlineData("mp3-192", "mp3-192")]
    [InlineData("original-flac", "original-opus")] // codec que o vídeo não tem: cai no outro original
    [InlineData("coisa-velha", "mp3-320")]
    [InlineData(null, "mp3-320")]
    public void Escolhe_a_opcao_salva_ou_a_mais_proxima(string? salva, string esperada) =>
        Assert.Equal(esperada, Formatos.Escolher(Formatos.Montar(Audio), salva).Chave);

    [Fact]
    public void Original_salvo_sem_analise_volta_ao_MP3_320() =>
        Assert.Equal("mp3-320", Formatos.Escolher(Formatos.Montar(null), "original-opus").Chave);

    [Fact]
    public void Explica_que_MP3_acima_da_fonte_nao_melhora_o_som()
    {
        var texto = Formatos.Explicar(Formatos.Montar(Audio).First(o => o.Kbps == 320), Audio);
        Assert.Contains("Opus 130 kbps", texto);
        Assert.Contains("não fica melhor", texto);
    }

    [Fact]
    public void Explica_que_MP3_abaixo_da_fonte_perde_qualidade() =>
        Assert.Contains("perde um pouco", Formatos.Explicar(Formatos.Montar(Audio).First(o => o.Kbps == 128), Audio));

    [Theory]
    [InlineData(213, "3:33")]
    [InlineData(59.6, "1:00")]
    [InlineData(3725, "1:02:05")]
    public void Formata_a_duracao(double segundos, string texto) =>
        Assert.Equal(texto, Formatos.Duracao(segundos));
}
