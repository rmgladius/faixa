namespace Faixa.Testes;

public class MetadadosTestes
{
    [Theory]
    [InlineData("Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster)", "Rick Astley - Never Gonna Give You Up")]
    [InlineData("Anitta - Envolver [Official Music Video]", "Anitta - Envolver")]
    [InlineData("Henrique e Juliano - Liberdade Provisória (Ao Vivo) (Clipe Oficial)", "Henrique e Juliano - Liberdade Provisória (Ao Vivo)")]
    [InlineData("Queen – Bohemian Rhapsody (Remastered 2011)", "Queen – Bohemian Rhapsody (Remastered 2011)")]
    [InlineData("Adele - Hello (Lyrics)", "Adele - Hello")]
    [InlineData("Artista - Música | Clipe Oficial", "Artista - Música")]
    [InlineData("Artista - Música - Official Video", "Artista - Música")]
    [InlineData("Música Boa (Áudio Oficial) #sertanejo #2024", "Música Boa")]
    [InlineData("Banda - Hino [HD] (Lyric Video)", "Banda - Hino")]
    [InlineData("Drake - Hotline Bling (feat. Ninguém)", "Drake - Hotline Bling (feat. Ninguém)")]
    [InlineData("Daft Punk - Get Lucky (Official Video) ft. Pharrell Williams, Nile Rodgers", "Daft Punk - Get Lucky ft. Pharrell Williams, Nile Rodgers")]
    [InlineData("Artista - Música【Official Video】", "Artista - Música")]
    [InlineData("(Official Video)", "(Official Video)")] // tudo era ruído: devolve o original
    public void Limpa_o_titulo(string titulo, string esperado) =>
        Assert.Equal(esperado, Metadados.LimparTitulo(titulo));

    [Theory]
    [InlineData("RickAstleyVEVO", "Rick Astley")]
    [InlineData("Tom Jobim - Topic", "Tom Jobim")]
    [InlineData("Anitta Oficial", "Anitta")]
    [InlineData("Coldplay", "Coldplay")]
    [InlineData("AC/DC VEVO", "AC/DC")]
    public void Limpa_o_nome_do_canal(string canal, string esperado) =>
        Assert.Equal(esperado, Metadados.LimparCanal(canal));

    [Fact]
    public void Separa_artista_e_musica_pelo_traco()
    {
        var nome = Metadados.Resolver(new DadosMusica("Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster)", "Rick Astley"));

        Assert.Equal("Rick Astley", nome.Artista);
        Assert.Equal("Never Gonna Give You Up", nome.Musica);
        Assert.Equal(OrigemNome.Titulo, nome.Origem);
    }

    [Fact]
    public void Inverte_quando_o_artista_esta_depois_do_traco()
    {
        var nome = Metadados.Resolver(new DadosMusica("Liberdade Provisória - Henrique e Juliano (Clipe Oficial)", "Henrique e Juliano"));

        Assert.Equal("Henrique e Juliano", nome.Artista);
        Assert.Equal("Liberdade Provisória", nome.Musica);
    }

    [Fact]
    public void Separa_artista_e_musica_entre_aspas()
    {
        var nome = Metadados.Resolver(new DadosMusica("Anitta \"Envolver\" (Official Music Video)", "Anitta"));

        Assert.Equal("Anitta", nome.Artista);
        Assert.Equal("Envolver", nome.Musica);
    }

    [Fact]
    public void Sem_separacao_usa_o_canal_como_artista()
    {
        var nome = Metadados.Resolver(new DadosMusica("Evidências (Ao Vivo)", "ChitãozinhoEXororoVEVO"));

        Assert.Equal("Chitãozinho EXororo", nome.Artista); // CamelCase separa onde dá; a tela deixa corrigir
        Assert.Equal("Evidências (Ao Vivo)", nome.Musica);
        Assert.Equal(OrigemNome.Canal, nome.Origem);
    }

    [Fact]
    public void Dados_oficiais_vencem_o_titulo()
    {
        var nome = Metadados.Resolver(new DadosMusica("Garota de Ipanema (Remasterizado)", "Tom Jobim - Topic",
            Faixa: "Garota de Ipanema", Artistas: "Tom Jobim, Vinicius de Moraes", Album: "Getz/Gilberto", Ano: 1964));

        Assert.Equal("Tom Jobim, Vinicius de Moraes", nome.Artista);
        Assert.Equal("Garota de Ipanema", nome.Musica);
        Assert.Equal("Getz/Gilberto", nome.Album);
        Assert.Equal(1964, nome.Ano);
        Assert.Equal(OrigemNome.Oficial, nome.Origem);
    }

    [Fact]
    public void Faixa_oficial_sem_artista_usa_o_canal()
    {
        var nome = Metadados.Resolver(new DadosMusica("Qualquer", "Tom Jobim - Topic", Faixa: "Wave"));

        Assert.Equal("Tom Jobim", nome.Artista);
        Assert.Equal(OrigemNome.Oficial, nome.Origem);
    }

    [Theory]
    [InlineData("AC/DC", "Back In Black", null, "AC-DC - Back In Black.mp3")]
    [InlineData("Artista", "Interstellar: Main Theme", null, "Artista - Interstellar - Main Theme.mp3")]
    [InlineData("Artista", "Why? \"Live\" <2024>*", null, "Artista - Why 'Live' 2024.mp3")]
    [InlineData("", "Só a música", null, "Só a música.mp3")]
    [InlineData("Banda", "Abertura", 3, "03. Banda - Abertura.mp3")]
    public void Gera_nome_de_arquivo_valido_no_Windows(string artista, string musica, int? numero, string esperado) =>
        Assert.Equal(esperado, Metadados.NomeDeArquivo(new NomeMusica(artista, musica, null, null, OrigemNome.Editado), "mp3", numero));

    [Fact]
    public void Nome_reservado_do_Windows_ganha_sufixo() =>
        Assert.Equal("CON_", Metadados.Sanitizar("CON"));
}
