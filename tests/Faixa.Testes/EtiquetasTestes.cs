namespace Faixa.Testes;

public class EtiquetasTestes : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("faixa-testes-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    /// <summary>MP3 mínimo e válido: 20 quadros MPEG-1 Layer III de 128 kbps / 44,1 kHz em silêncio.</summary>
    private string CriarMp3()
    {
        var caminho = Path.Combine(_pasta, "teste.mp3");
        var quadro = new byte[417]; // 144 × 128000 / 44100
        quadro[0] = 0xFF;
        quadro[1] = 0xFB;
        quadro[2] = 0x90;
        quadro[3] = 0x64;
        using var arquivo = File.Create(caminho);
        for (var i = 0; i < 20; i++)
            arquivo.Write(quadro);
        return caminho;
    }

    [Fact]
    public void Grava_e_le_as_informacoes_em_ID3v2_3()
    {
        var mp3 = CriarMp3();
        Etiquetas.Gravar(mp3, new NomeMusica("Tom Jobim, Vinicius de Moraes", "Garota de Ipanema", "Getz/Gilberto", 1964, OrigemNome.Oficial), faixa: 5);

        var lido = Etiquetas.Ler(mp3);
        Assert.Equal("Tom Jobim, Vinicius de Moraes", lido.Artista);
        Assert.Equal("Garota de Ipanema", lido.Musica);
        Assert.Equal("Getz/Gilberto", lido.Album);
        Assert.Equal(5u, lido.Faixa);
        Assert.Equal(3, lido.VersaoId3);
    }

    [Fact]
    public void Sem_album_nao_apaga_o_que_ja_estava_la()
    {
        var mp3 = CriarMp3();
        Etiquetas.Gravar(mp3, new NomeMusica("A", "B", "Álbum do yt-dlp", null, OrigemNome.Oficial));
        Etiquetas.Gravar(mp3, new NomeMusica("Artista", "Música", null, null, OrigemNome.Titulo));

        Assert.Equal("Álbum do yt-dlp", Etiquetas.Ler(mp3).Album);
    }
}
