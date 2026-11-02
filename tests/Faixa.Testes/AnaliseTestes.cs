namespace Faixa.Testes;

public class AnaliseTestes
{
    [Fact]
    public void Escolhe_o_Opus_quando_empata_com_o_AAC_na_qualidade_do_YouTube()
    {
        var audio = Analisador.Interpretar(ExemplosYtDlp.Clipe)!;

        Assert.Equal("251", audio.Melhor.Id);
        Assert.Equal("opus", audio.Melhor.Codec);
        Assert.Equal(48000, audio.Melhor.Hz);
        Assert.Equal(213, audio.Duracao);
    }

    [Fact]
    public void Ignora_DRC_formatos_com_video_e_storyboard()
    {
        var audio = Analisador.Interpretar(ExemplosYtDlp.Clipe)!;
        var ids = audio.Originais.Select(f => f.Id).ToList();

        Assert.DoesNotContain("251-drc", ids);
        Assert.DoesNotContain("18", ids);
        Assert.DoesNotContain("sb0", ids);
    }

    [Fact]
    public void Lista_os_originais_com_o_Opus_antes_do_AAC()
    {
        var audio = Analisador.Interpretar(ExemplosYtDlp.Clipe)!;

        Assert.Equal(["251", "140"], audio.Originais.Select(f => f.Id));
    }

    [Fact]
    public void Prefere_o_audio_original_a_uma_dublagem_com_taxa_maior()
    {
        var audio = Analisador.Interpretar(ExemplosYtDlp.Dublado)!;

        Assert.Equal("251-0", audio.Melhor.Id);
        Assert.All(audio.Originais, f => Assert.EndsWith("-0", f.Id));
    }

    [Fact]
    public void Sem_formato_de_audio_devolve_nulo() =>
        Assert.Null(Analisador.Interpretar(ExemplosYtDlp.SemAudio));

    [Theory]
    [InlineData("opus", "opus", "Opus")]
    [InlineData("mp4a.40.2", "aac", "AAC")]
    [InlineData("mp4a.40.5", "aac", "AAC")]
    [InlineData("vorbis", "vorbis", "Vorbis")]
    [InlineData("ec-3", "ec-3", "Dolby E-AC-3")]
    public void Normaliza_e_da_nome_aos_codecs(string acodec, string codec, string nome)
    {
        Assert.Equal(codec, Analisador.NormalizarCodec(acodec));
        Assert.Equal(nome, Analisador.NomeDoCodec(codec));
    }
}
