namespace Faixa.Testes;

public class LinksDePlataformasTestes
{
    [Theory]
    [InlineData("https://open.spotify.com/track/4PTG3Z6ehGkBFwjybzWkR8?si=abc123", TipoCatalogo.Faixa, "4PTG3Z6ehGkBFwjybzWkR8")]
    [InlineData("https://open.spotify.com/intl-pt/track/4PTG3Z6ehGkBFwjybzWkR8", TipoCatalogo.Faixa, "4PTG3Z6ehGkBFwjybzWkR8")]
    [InlineData("open.spotify.com/album/2noRn2Aes5aoNVsU6iWThc", TipoCatalogo.Album, "2noRn2Aes5aoNVsU6iWThc")]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M", TipoCatalogo.Playlist, "37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("https://open.spotify.com/embed/playlist/37i9dQZF1DXcBWIGoYBM5M?utm_source=generator", TipoCatalogo.Playlist, "37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("spotify:track:4PTG3Z6ehGkBFwjybzWkR8", TipoCatalogo.Faixa, "4PTG3Z6ehGkBFwjybzWkR8")]
    public void Reconhece_links_do_Spotify(string texto, TipoCatalogo tipo, string id) =>
        Assert.Equal(new LinkCatalogo(Plataforma.Spotify, tipo, id), Links.Reconhecer(texto));

    [Theory]
    [InlineData("https://www.deezer.com/br/track/3135556", TipoCatalogo.Faixa, "3135556")]
    [InlineData("https://www.deezer.com/track/3135556?utm_source=deezer", TipoCatalogo.Faixa, "3135556")]
    [InlineData("deezer.com/en/album/302127", TipoCatalogo.Album, "302127")]
    [InlineData("https://www.deezer.com/pt-br/playlist/908622995", TipoCatalogo.Playlist, "908622995")]
    public void Reconhece_links_do_Deezer(string texto, TipoCatalogo tipo, string id) =>
        Assert.Equal(new LinkCatalogo(Plataforma.Deezer, tipo, id), Links.Reconhecer(texto));

    [Theory]
    [InlineData("https://soundcloud.com/the-concept-band/gimme-twice-mastered", "https://soundcloud.com/the-concept-band/gimme-twice-mastered", false)]
    [InlineData("https://m.soundcloud.com/the-concept-band/gimme-twice-mastered?in=the-concept-band/sets/the-royal-concept-ep",
        "https://soundcloud.com/the-concept-band/gimme-twice-mastered", false)]
    [InlineData("soundcloud.com/artista/faixa-privada/s-AbCdE", "https://soundcloud.com/artista/faixa-privada/s-AbCdE", false)]
    [InlineData("https://soundcloud.com/the-concept-band/sets/the-royal-concept-ep?si=x", "https://soundcloud.com/the-concept-band/sets/the-royal-concept-ep", true)]
    [InlineData("https://api.soundcloud.com/tracks/47127625", "https://api.soundcloud.com/tracks/47127625", false)]
    public void Reconhece_faixas_e_sets_do_SoundCloud(string texto, string url, bool set) =>
        Assert.Equal(new LinkSoundCloud(url, set), Links.Reconhecer(texto));

    [Theory]
    [InlineData("https://spotify.link/AbCdEf123", Plataforma.Spotify)]
    [InlineData("https://link.deezer.com/s/31bWrQxCp2Ul6Vzye4bCf", Plataforma.Deezer)]
    [InlineData("https://deezer.page.link/AbCd1234", Plataforma.Deezer)]
    [InlineData("https://on.soundcloud.com/AbCdEf", Plataforma.SoundCloud)]
    public void Links_curtos_ficam_para_abrir_depois(string texto, Plataforma plataforma)
    {
        var link = Assert.IsType<LinkCurto>(Links.Reconhecer(texto));
        Assert.Equal(plataforma, link.Plataforma);
    }

    [Theory]
    [InlineData("https://open.spotify.com/artist/0gxyHStUsqpMadRV0Di1Qt")]
    [InlineData("https://open.spotify.com/episode/0gxyHStUsqpMadRV0Di1Qt")]
    [InlineData("https://www.deezer.com/br/artist/27")]
    [InlineData("https://soundcloud.com/the-concept-band")]
    [InlineData("https://soundcloud.com/the-concept-band/likes")]
    public void Paginas_sem_musica_explicam_o_que_falta(string texto) =>
        Assert.IsType<LinkSemSuporte>(Links.Reconhecer(texto));

    [Theory]
    [InlineData("https://open.spotify.com/track/curto")]
    [InlineData("https://www.deezer.com/br/track/abc")]
    [InlineData("https://soundcloud.com/discover")]
    [InlineData("https://soundcloud.com/search?q=musica")]
    [InlineData("https://exemplo.com/musica")]
    [InlineData("musica legal")]
    public void Recusa_o_que_nao_e_musica(string texto) =>
        Assert.Null(Links.Reconhecer(texto));

    [Fact]
    public void YouTube_continua_igual() =>
        Assert.Equal(new LinkYouTube("dQw4w9WgXcQ", null), Links.Reconhecer("https://youtu.be/dQw4w9WgXcQ"));

    [Fact]
    public void Acha_o_destino_de_um_link_curto_dentro_da_pagina()
    {
        const string pagina = """
            <html><head><meta property="og:url" content="https://www.deezer.com/br/track/3135556?host=0&amp;utm_campaign=x"></head>
            <script>var d = {"url":"https:\/\/www.deezer.com\/br\/track\/3135556"};</script></html>
            """;
        Assert.Equal(new LinkCatalogo(Plataforma.Deezer, TipoCatalogo.Faixa, "3135556"), Links.DestinoNaPagina(pagina, Plataforma.Deezer));
    }
}

public class CatalogoTestes
{
    [Fact]
    public void Le_musica_do_Spotify()
    {
        var info = Spotify.Interpretar(Exemplos.Ler("spotify-faixa.html"), TipoCatalogo.Faixa)!;
        var faixa = Assert.Single(info.Faixas);

        Assert.Equal("Never Gonna Give You Up", faixa.Titulo);
        Assert.Equal(["Rick Astley"], faixa.Artistas);
        Assert.Equal(213.6, faixa.Duracao);
        Assert.Equal(1987, faixa.Ano);
        Assert.Null(faixa.Isrc);
        Assert.Contains("ab67616d0000b273", faixa.CapaUrl); // a de 640 px, a maior
    }

    [Fact]
    public void Le_album_do_Spotify_com_album_e_capa_em_cada_faixa()
    {
        var info = Spotify.Interpretar(Exemplos.Ler("spotify-album.html"), TipoCatalogo.Album)!;

        Assert.Equal("Discovery", info.Titulo);
        Assert.Equal("Daft Punk", info.Autor);
        Assert.Equal(["One More Time", "Aerodynamic", "Digital Love", "Harder, Better, Faster, Stronger"], info.Faixas.Select(f => f.Titulo));
        Assert.All(info.Faixas, f => Assert.Equal("Discovery", f.Album));
        Assert.All(info.Faixas, f => Assert.Equal(info.CapaUrl, f.CapaUrl));
        Assert.False(info.Cortada);
    }

    [Fact]
    public void Le_playlist_do_Spotify()
    {
        var info = Spotify.Interpretar(Exemplos.Ler("spotify-playlist.html"), TipoCatalogo.Playlist)!;

        Assert.Equal("500 Greatest Songs Of All Time", info.Titulo);
        Assert.Equal("kieron", info.Autor);
        Assert.Equal(6, info.Faixas.Count);
        Assert.Equal("Here Comes The Sun - Remastered 2009", info.Faixas[1].Titulo);
        Assert.All(info.Faixas, f => Assert.Null(f.Album)); // playlist não diz o álbum de cada faixa
        Assert.NotNull(info.CapaUrl);
        Assert.False(info.Cortada); // só 6 no exemplo; com 100 seria
    }

    [Fact]
    public void Separa_artistas_do_Spotify_sem_quebrar_nomes_com_virgula()
    {
        Assert.Equal(["Blue Swede", "Björn Skifs"], Spotify.Artistas("Blue Swede, Björn Skifs"));
        Assert.Equal(["Earth, Wind & Fire"], Spotify.Artistas("Earth, Wind & Fire"));
    }

    [Fact]
    public void Pagina_do_Spotify_sem_dados_nao_e_musica() =>
        Assert.Null(Spotify.Interpretar("<html><body>Página não encontrada</body></html>", TipoCatalogo.Faixa));

    [Fact]
    public void Le_musica_do_Deezer_com_ISRC()
    {
        var faixa = Assert.Single(Deezer.Interpretar(Exemplos.Ler("deezer-faixa.json"), TipoCatalogo.Faixa)!.Faixas);

        Assert.Equal("Harder, Better, Faster, Stronger", faixa.Titulo);
        Assert.Equal(["Daft Punk"], faixa.Artistas);
        Assert.Equal(226, faixa.Duracao);
        Assert.Equal("GBDUW0000059", faixa.Isrc);
        Assert.Equal("Discovery", faixa.Album);
        Assert.Equal(2001, faixa.Ano);
    }

    [Fact]
    public void Le_album_do_Deezer()
    {
        var info = Deezer.Interpretar(Exemplos.Ler("deezer-album.json"), TipoCatalogo.Album)!;

        Assert.Equal("Discovery", info.Titulo);
        Assert.Equal("Daft Punk", info.Autor);
        Assert.Equal(4, info.Faixas.Count);
        Assert.All(info.Faixas, f => Assert.Equal("Discovery", f.Album));
        Assert.All(info.Faixas, f => Assert.Equal(2001, f.Ano));
        Assert.All(info.Faixas, f => Assert.EndsWith("1000x1000-000000-80-0-0.jpg", f.CapaUrl));
    }

    [Fact]
    public void Le_playlist_do_Deezer_com_album_e_capa_de_cada_faixa()
    {
        var info = Deezer.Interpretar(Exemplos.Ler("deezer-playlist.json"), TipoCatalogo.Playlist)!;

        Assert.Equal("En mode 60", info.Titulo);
        Assert.Equal("Hey Jude (Remastered 2015)", info.Faixas[0].Titulo);
        Assert.Equal("1 (Remastered 2015)", info.Faixas[0].Album);
        Assert.Equal("GBUM71505902", info.Faixas[0].Isrc);
        Assert.NotEqual(info.Faixas[0].CapaUrl, info.Faixas[1].CapaUrl);
    }

    [Fact]
    public void Erro_do_Deezer_nao_e_musica()
    {
        const string erro = """{"error":{"type":"DataException","message":"no data","code":800}}""";
        Assert.Null(Deezer.Interpretar(erro, TipoCatalogo.Faixa));
        Assert.False(Deezer.LimiteDePedidos(erro));
        Assert.True(Deezer.LimiteDePedidos("""{"error":{"type":"Exception","message":"Quota limit exceeded","code":4}}"""));
    }

    [Fact]
    public void Lista_de_catalogo_leva_a_faixa_em_cada_linha()
    {
        var info = Deezer.Interpretar(Exemplos.Ler("deezer-album.json"), TipoCatalogo.Album)!;
        var lista = Catalogo.ParaLista(info, "302127");

        Assert.True(lista.EhAlbum);
        Assert.Equal(Plataforma.Deezer, lista.Origem);
        Assert.Equal([1, 2, 3, 4], lista.Itens.Select(i => i.Numero));
        Assert.All(lista.Itens, i => Assert.NotNull(i.Catalogo));
        Assert.Equal(1059, lista.DuracaoTotal);
    }

    [Fact]
    public void Nome_oficial_usa_o_album_do_YouTube_Music_so_quando_falta()
    {
        var semAlbum = new FaixaCatalogo("Never Gonna Give You Up", ["Rick Astley"], 213.6);
        var comAlbum = semAlbum with { Album = "Whenever You Need Somebody" };

        Assert.Equal("Reserva", Catalogo.Nome(semAlbum, "Reserva").Album);
        Assert.Equal("Whenever You Need Somebody", Catalogo.Nome(comAlbum, "Reserva").Album);
        Assert.Equal(OrigemNome.Catalogo, Catalogo.Nome(semAlbum).Origem);
        Assert.Equal("Rick Astley - Never Gonna Give You Up", Catalogo.Nome(semAlbum).Completo);
    }
}

public class YouTubeMusicTestes
{
    [Fact]
    public void Le_titulo_artista_album_e_duracao_de_cada_resultado()
    {
        var resultados = YouTubeMusic.Interpretar(Exemplos.Ler("ytm-rick.json"));

        Assert.Equal(6, resultados.Count);
        var primeiro = resultados[0];
        Assert.Equal("lYBUbBu4W08", primeiro.VideoId);
        Assert.Equal("Never Gonna Give You Up", primeiro.Titulo);
        Assert.Equal("Rick Astley", primeiro.Artistas);
        Assert.Equal("Whenever You Need Somebody", primeiro.Album);
        Assert.Equal(214, primeiro.Duracao);
    }

    [Fact]
    public void Ignora_a_secao_de_sugestao_de_busca()
    {
        // A busca pelo ISRC vem com uma seção "você quis dizer" antes dos resultados.
        var resultados = YouTubeMusic.Interpretar(Exemplos.Ler("ytm-isrc-daft-punk.json"));

        Assert.Equal(3, resultados.Count);
        Assert.Equal("Harder, Better, Faster, Stronger", resultados[0].Titulo);
    }

    [Fact]
    public void Artista_sem_pagina_tambem_conta()
    {
        // "Ivan Papetti" vem como texto puro, sem link para a página do artista.
        var ultimo = YouTubeMusic.Interpretar(Exemplos.Ler("ytm-rick.json"))[^1];
        Assert.Equal("Ivan Papetti", ultimo.Artistas);
        Assert.Equal(185, ultimo.Duracao);
    }

    [Theory]
    [InlineData("3:34", 214)]
    [InlineData("1:02:03", 3723)]
    [InlineData("12:00", 720)]
    public void Le_a_duracao(string texto, double segundos) => Assert.Equal(segundos, YouTubeMusic.Tempo(texto));

    [Fact]
    public void Texto_qualquer_nao_e_duracao() => Assert.Null(YouTubeMusic.Tempo("Tocou 2 bi vezes"));
}

public class CorrespondenciaTestes
{
    private static IReadOnlyList<ResultadoBusca> Busca(string nome) => YouTubeMusic.Interpretar(Exemplos.Ler(nome));

    [Fact]
    public void Acha_a_gravacao_original_e_nao_a_regravacao()
    {
        var faixa = new FaixaCatalogo("Never Gonna Give You Up", ["Rick Astley"], 213.6);
        Assert.Equal("lYBUbBu4W08", Correspondencia.Escolher(faixa, Busca("ytm-rick.json"))?.VideoId);
    }

    [Fact]
    public void Artista_que_so_divide_o_primeiro_nome_nao_serve()
    {
        // "Rick Roll" tem o mesmo nome de música, quase a mesma duração e divide "Rick" com Rick Astley.
        var faixa = new FaixaCatalogo("Never Gonna Give You Up", ["Rick Astley"], 213.6);
        var rickRoll = Busca("ytm-rick.json")[1];
        Assert.Equal("Rick Roll", rickRoll.Artistas);
        Assert.Equal(0, Correspondencia.Nota(faixa, rickRoll));
    }

    [Fact]
    public void Remaster_do_Spotify_escolhe_o_mesmo_remaster()
    {
        var faixa = new FaixaCatalogo("Here Comes The Sun - Remastered 2009", ["The Beatles"], 185.7);
        var achada = Correspondencia.Escolher(faixa, Busca("ytm-beatles.json"))!;

        Assert.Equal("Here Comes The Sun (Remastered 2009)", achada.Titulo); // e não o "(2019 Mix)"
    }

    [Fact]
    public void Banda_de_tributo_nao_passa_pela_original()
    {
        var faixa = new FaixaCatalogo("Here Comes The Sun - Remastered 2009", ["The Beatles"], 185.7);
        var tributo = Busca("ytm-beatles.json")[3];
        Assert.Equal("The Bornagen Beatles", tributo.Artistas);
        Assert.True(Correspondencia.Nota(faixa, tributo) < Correspondencia.NotaMinima);
    }

    [Fact]
    public void Duracao_decide_entre_versoes_do_mesmo_artista()
    {
        // Quatro "Garota de Ipanema" do Jobim: a de 2:41 é a que o serviço tem.
        var faixa = new FaixaCatalogo("Garota De Ipanema", ["Antônio Carlos Jobim"], 161);
        Assert.Equal("uW5acE-5280", Correspondencia.Escolher(faixa, Busca("ytm-jobim.json"))?.VideoId);
    }

    [Fact]
    public void Sobrenome_basta_quando_nome_e_duracao_batem()
    {
        var faixa = new FaixaCatalogo("Garota de Ipanema", ["Tom Jobim"], 243);
        Assert.Equal("WuenyQ4NCQE", Correspondencia.Escolher(faixa, Busca("ytm-jobim.json"))?.VideoId);
    }

    [Fact]
    public void Busca_pelo_ISRC_confere_o_resultado()
    {
        var faixa = new FaixaCatalogo("Harder, Better, Faster, Stronger", ["Daft Punk"], 226, Isrc: "GBDUW0000059");
        var resultados = Busca("ytm-isrc-daft-punk.json");

        Assert.Equal("JhulBGMA7G4", Correspondencia.Escolher(faixa, resultados)?.VideoId);
        Assert.Equal(0, Correspondencia.Nota(faixa, resultados[1])); // "Luz De Um Sono Profundo"
    }

    [Fact]
    public void Edicao_de_radio_com_a_mesma_duracao_serve()
    {
        var faixa = new FaixaCatalogo("One More Time", ["Daft Punk"], 320.4, Album: "Discovery");
        Assert.Equal("wU26xVT_vBU", Correspondencia.Escolher(faixa, Busca("ytm-one-more-time.json"))?.VideoId);
    }

    [Fact]
    public void Sem_resultado_confiavel_nao_escolhe_nada()
    {
        var outra = new FaixaCatalogo("Bohemian Rhapsody", ["Queen"], 354);
        Assert.Null(Correspondencia.Escolher(outra, Busca("ytm-rick.json")));
    }

    [Theory]
    [InlineData("Never Gonna Give You Up (Live)")]
    [InlineData("Never Gonna Give You Up - Acoustic")]
    [InlineData("Never Gonna Give You Up (Instrumental)")]
    [InlineData("Never Gonna Give You Up (Karaoke Version)")]
    [InlineData("Never Gonna Give You Up (Sped Up)")]
    public void Recusa_outra_gravacao_da_mesma_musica(string titulo)
    {
        var faixa = new FaixaCatalogo("Never Gonna Give You Up", ["Rick Astley"], 213.6);
        var r = new ResultadoBusca("aaaaaaaaaaa", titulo, "Rick Astley", null, 214);
        Assert.True(Correspondencia.Nota(faixa, r) < Correspondencia.NotaMinima);
    }

    [Fact]
    public void Aceita_ao_vivo_quando_a_original_tambem_e()
    {
        var faixa = new FaixaCatalogo("Never Gonna Give You Up - Live", ["Rick Astley"], 230);
        var r = new ResultadoBusca("aaaaaaaaaaa", "Never Gonna Give You Up (Live)", "Rick Astley", null, 231);
        Assert.True(Correspondencia.Nota(faixa, r) >= Correspondencia.NotaMinima);
    }

    [Fact]
    public void Duracao_muito_diferente_e_outra_gravacao()
    {
        var faixa = new FaixaCatalogo("Never Gonna Give You Up", ["Rick Astley"], 213.6);
        var estendida = new ResultadoBusca("aaaaaaaaaaa", "Never Gonna Give You Up", "Rick Astley", null, 400);
        Assert.Equal(0, Correspondencia.Nota(faixa, estendida));
    }

    [Theory]
    [InlineData("Here Comes The Sun - Remastered 2009", "Here Comes The Sun")]
    [InlineData("Here Comes The Sun (Remastered 2009)", "Here Comes The Sun")]
    [InlineData("Stay With Me", "Stay With Me")]
    [InlineData("Empire State of Mind (feat. Alicia Keys)", "Empire State of Mind")]
    [InlineData("Song feat. Someone", "Song")]
    public void Nome_base_tira_so_a_versao(string titulo, string esperado) =>
        Assert.Equal(esperado, Correspondencia.Base(titulo));

    [Fact]
    public void Participacao_nao_conta_como_versao_diferente()
    {
        var faixa = new FaixaCatalogo("Empire State of Mind (feat. Alicia Keys)", ["JAY-Z", "Alicia Keys"], 276);
        var r = new ResultadoBusca("aaaaaaaaaaa", "Empire State of Mind (feat. Alicia Keys)", "JAY-Z & Alicia Keys", "The Blueprint 3", 277);
        Assert.True(Correspondencia.Nota(faixa, r) > 0.95);
    }
}

public class SoundCloudTestes
{
    private const string UrlSet = "https://soundcloud.com/the-concept-band/sets/the-royal-concept-ep";

    [Fact]
    public void Le_o_set_da_pagina()
    {
        var lista = SoundCloud.InterpretarSet(Exemplos.Ler("soundcloud-set.html"), UrlSet)!;

        Assert.Equal("The Royal Concept EP", lista.Titulo);
        Assert.Equal("The Royal Concept", lista.Canal);
        Assert.True(lista.EhAlbum); // set_type "ep"
        Assert.Equal(Plataforma.SoundCloud, lista.Origem);
        Assert.Equal(6, lista.Itens.Count);
        Assert.Equal("https://soundcloud.com/the-concept-band/gimme-twice-mastered", lista.Itens[1].Url);
        Assert.Equal("The Royal Concept - Gimme Twice", lista.Itens[1].Titulo);
        Assert.Equal(205.3, lista.Itens[1].Duracao);
        Assert.EndsWith("-t500x500.jpg", lista.CapaUrl);
    }

    [Fact]
    public void Faixa_do_Go_plus_fica_indisponivel_com_o_motivo()
    {
        var lista = SoundCloud.InterpretarSet(Exemplos.Ler("soundcloud-set.html"), UrlSet)!;

        Assert.Equal([false, true, false, true, false, true], lista.Itens.Select(i => i.Disponivel));
        Assert.Equal(SoundCloud.AvisoGoPlus, lista.Itens[0].Motivo);
        Assert.Equal(200, lista.Itens[0].Duracao); // a duração inteira, não a do trecho
    }

    [Fact]
    public void Faixa_so_com_codigo_fica_para_o_oEmbed()
    {
        var ultima = SoundCloud.InterpretarSet(Exemplos.Ler("soundcloud-set.html"), UrlSet)!.Itens[^1];

        Assert.Equal("", ultima.Titulo);
        Assert.Equal("https://api.soundcloud.com/tracks/47127631", ultima.Url);
    }

    [Fact]
    public void Pagina_sem_os_dados_cai_no_yt_dlp() =>
        Assert.Null(SoundCloud.InterpretarSet("<html></html>", UrlSet));

    [Theory]
    [InlineData("The Royal Concept - Gimme Twice by The Royal Concept", "The Royal Concept", "The Royal Concept - Gimme Twice")]
    [InlineData("Stand by Me by Fulano", "Fulano", "Stand by Me")]
    [InlineData("Stand by Me", "Ciclano", "Stand by Me")]
    public void Tira_o_autor_do_titulo_do_oEmbed(string titulo, string autor, string esperado) =>
        Assert.Equal(esperado, SoundCloud.TirarAutor(titulo, autor));

    private const string JsonFaixa = """
        {"id": "47127625", "title": "The Royal Concept - Gimme Twice", "track": "The Royal Concept - Gimme Twice",
         "uploader": "The Royal Concept", "duration": 205.348, "extractor_key": "Soundcloud", "formats": [
          {"format_id": "hls_mp3_0_1", "ext": "mp3", "acodec": "mp3", "vcodec": "none", "abr": null, "quality": -1, "protocol": "m3u8_native"},
          {"format_id": "hls_opus_0_0", "ext": "opus", "acodec": "opus", "vcodec": "none", "abr": 64, "quality": -1, "protocol": "m3u8_native"},
          {"format_id": "hls_aac_160k", "ext": "m4a", "acodec": "mp4a.40.2", "vcodec": "none", "abr": 160, "quality": 0, "protocol": "m3u8_native"}
        ]}
        """;

    [Fact]
    public void Analise_do_SoundCloud_escolhe_o_AAC_de_160()
    {
        var audio = Analisador.Interpretar(JsonFaixa)!;

        Assert.Equal("hls_aac_160k", audio.Melhor.Id);
        Assert.Equal(["hls_aac_160k", "hls_opus_0_0"], audio.Originais.Select(f => f.Id));
    }

    [Fact]
    public void Nome_do_SoundCloud_sai_do_titulo_e_nao_repete_o_artista()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(JsonFaixa);
        var nome = Metadados.Resolver(Analisador.LerDados(doc.RootElement));

        Assert.Equal("The Royal Concept", nome.Artista);
        Assert.Equal("Gimme Twice", nome.Musica);
    }

    [Fact]
    public void So_trecho_de_30_segundos_avisa_do_Go_plus()
    {
        const string json = """
            {"id": "75206121", "title": "World On Fire", "duration": 30, "extractor_key": "Soundcloud", "formats": [
              {"format_id": "hls_aac_160k_preview", "ext": "m4a", "acodec": "mp4a.40.2", "vcodec": "none", "abr": 160, "preference": -10}
            ]}
            """;
        Assert.Null(Analisador.Interpretar(json));
        var erro = Analisador.SemAudio(json);
        Assert.Equal(TipoErro.Previa, erro.Tipo);
        Assert.Equal(SoundCloud.AvisoGoPlus, erro.Message);
    }

    [Fact]
    public void Arquivo_original_sem_codec_nem_taxa_usa_a_extensao_e_o_tamanho()
    {
        const string json = """
            {"id": "1", "title": "Faixa", "duration": 100, "extractor_key": "Soundcloud", "formats": [
              {"format_id": "download", "ext": "wav", "vcodec": "none", "quality": 10, "filesize": 17640000},
              {"format_id": "hls_aac_160k", "ext": "m4a", "acodec": "mp4a.40.2", "vcodec": "none", "abr": 160, "quality": 0}
            ]}
            """;
        var audio = Analisador.Interpretar(json)!;

        Assert.Equal("download", audio.Melhor.Id);
        Assert.Equal("wav", audio.Melhor.Codec);
        Assert.Equal(1411.2, audio.Melhor.Kbps, 1);
    }

    [Theory]
    [InlineData("ERROR: [soundcloud] 123: Unable to download JSON metadata: HTTP Error 404: Not Found", TipoErro.Indisponivel)]
    [InlineData("ERROR: [soundcloud] 123: HTTP Error 429: Too Many Requests", TipoErro.Bloqueio)]
    [InlineData("ERROR: [soundcloud] 123: This video is DRM protected", TipoErro.Restrito)]
    public void Traduz_erros_do_SoundCloud(string linha, TipoErro tipo) =>
        Assert.Equal(tipo, Baixador.Traduzir([linha]).Tipo);

    [Fact]
    public void Limite_do_SoundCloud_nao_fala_em_YouTube() =>
        Assert.DoesNotContain("YouTube", Baixador.Traduzir(["ERROR: [soundcloud] 1: HTTP Error 429: Too Many Requests"]).Message);

    [Fact]
    public void Textos_de_formato_dizem_a_fonte_certa()
    {
        var audio = Analisador.Interpretar(JsonFaixa)!;
        var mp3 = Formatos.Montar(audio, fonte: "SoundCloud").First(o => o.Kbps == 320);

        Assert.StartsWith("O SoundCloud tem AAC 160 kbps.", Formatos.Explicar(mp3, audio, "SoundCloud"));
        Assert.Equal("Original · melhor do SoundCloud", Formatos.Montar(null, 100, "SoundCloud")[^1].Titulo);
        Assert.Contains("YouTube Music", Formatos.ExplicarCatalogo(mp3));
    }
}
