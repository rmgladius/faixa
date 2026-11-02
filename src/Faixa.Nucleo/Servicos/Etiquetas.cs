namespace Faixa.Servicos;

/// <summary>
/// Grava artista, música, álbum, ano e número da faixa dentro do arquivo. O yt-dlp já deixa a capa
/// e os dados do vídeo; aqui eles são trocados pelos nomes limpos (ou editados na tela).
/// </summary>
public static class Etiquetas
{
    static Etiquetas()
    {
        // ID3v2.3: a versão que o Explorer e o Windows Media Player leem sem tropeçar.
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;
    }

    public static void Gravar(string arquivo, NomeMusica nome, int? faixa = null)
    {
        using var f = TagLib.File.Create(arquivo);
        f.Tag.Title = nome.Musica;
        f.Tag.Performers = string.IsNullOrWhiteSpace(nome.Artista) ? [] : [nome.Artista];
        if (nome.Album is { Length: > 0 } album)
            f.Tag.Album = album;
        if (nome.Ano is int ano and > 0)
            f.Tag.Year = (uint)ano;
        if (faixa is int n and > 0)
            f.Tag.Track = (uint)n;
        f.Save();
    }

    /// <summary>Lê de volta (usado nos testes e para conferir o resultado).</summary>
    public static (string? Artista, string? Musica, string? Album, uint Faixa, int VersaoId3) Ler(string arquivo)
    {
        using var f = TagLib.File.Create(arquivo);
        var id3 = f.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
        return (f.Tag.FirstPerformer, f.Tag.Title, f.Tag.Album, f.Tag.Track, id3?.Version ?? 0);
    }
}
