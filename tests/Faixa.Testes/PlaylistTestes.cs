namespace Faixa.Testes;

public class PlaylistTestes
{
    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLabcdefghijkl", null, "PLabcdefghijkl")]
    [InlineData("https://music.youtube.com/playlist?list=OLAK5uy_abcdefghijklmnop", null, "OLAK5uy_abcdefghijklmnop")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLabcdefghijkl&index=3", "dQw4w9WgXcQ", "PLabcdefghijkl")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?list=PLabcdefghijkl", "dQw4w9WgXcQ", "PLabcdefghijkl")]
    [InlineData("https://music.youtube.com/browse/MPREb_abcdefghij", null, "MPREb_abcdefghij")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ&list=RDCLAK5uy_abcdefghijkl", "dQw4w9WgXcQ", "RDCLAK5uy_abcdefghijkl")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=RDdQw4w9WgXcQ&start_radio=1", "dQw4w9WgXcQ", null)] // mix: ignora
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=LL", "dQw4w9WgXcQ", null)] // Curtidas: exige login
    public void Reconhece_video_e_playlist_no_link(string link, string? video, string? lista)
    {
        var l = YouTube.ExtrairLink(link)!;
        Assert.Equal(video, l.VideoId);
        Assert.Equal(lista, l.ListaId);
    }

    [Fact]
    public void Mix_sem_video_nao_e_link_valido() =>
        Assert.Null(YouTube.ExtrairLink("https://www.youtube.com/playlist?list=RDdQw4w9WgXcQ"));

    [Fact]
    public void Le_a_playlist_e_marca_privados_e_apagados_como_indisponiveis()
    {
        var lista = Playlists.Interpretar(ExemplosYtDlp.Playlist, "PLabcdefghijkl")!;

        Assert.Equal("Clássicos dos 80", lista.Titulo);
        Assert.False(lista.EhAlbum);
        Assert.Equal(4, lista.Itens.Count);
        Assert.Equal(2, lista.Disponiveis);
        Assert.Equal([true, false, true, false], lista.Itens.Select(i => i.Disponivel));
        Assert.Equal([1, 2, 3, 4], lista.Itens.Select(i => i.Numero));
        Assert.Equal(572, lista.DuracaoTotal);
        Assert.Equal("Vídeo privado", lista.Itens[1].Titulo);
        Assert.Equal("Vídeo apagado", lista.Itens[3].Titulo);
    }

    [Fact]
    public void Reconhece_album_do_YouTube_Music()
    {
        var lista = Playlists.Interpretar(ExemplosYtDlp.Album, "OLAK5uy_abcdefghijklmnop")!;

        Assert.True(lista.EhAlbum);
        Assert.Equal("Getz/Gilberto", lista.Titulo);
        Assert.Equal("Stan Getz", lista.Canal);
    }

    [Fact]
    public void Faixa_de_album_usa_o_canal_Topico_como_artista()
    {
        var item = Playlists.Interpretar(ExemplosYtDlp.Album, "OLAK5uy_abcdefghijklmnop")!.Itens[0];
        var nome = Metadados.Resolver(new DadosMusica(item.Titulo, item.Canal));

        Assert.Equal("Stan Getz", nome.Artista);
        Assert.Equal("The Girl from Ipanema", nome.Musica);
    }

    [Fact]
    public void Playlist_oferece_MP3_com_tamanho_total_e_um_original_generico()
    {
        var opcoes = Formatos.Montar(null, duracaoLista: 572);

        Assert.Equal(["mp3-320", "mp3-256", "mp3-192", "mp3-128", Formatos.OriginalGenerico], opcoes.Select(o => o.Chave));
        Assert.EndsWith("no total", opcoes[0].Tamanho);
        Assert.True(opcoes[^1].SemConversao);
        Assert.Null(opcoes[^1].Original);
    }

    [Theory]
    [InlineData("original-opus")]
    [InlineData("original-aac")]
    public void Original_escolhido_num_video_vira_o_generico_na_playlist(string salvo) =>
        Assert.Equal(Formatos.OriginalGenerico, Formatos.Escolher(Formatos.Montar(null, 572), salvo).Chave);

    [Fact]
    public void Erro_de_playlist_inexistente_tem_mensagem_propria() =>
        Assert.Equal("Essa playlist não existe ou é privada.",
            Baixador.Traduzir(["ERROR: [youtube:tab] PLx: The playlist does not exist."]).Message);
}
