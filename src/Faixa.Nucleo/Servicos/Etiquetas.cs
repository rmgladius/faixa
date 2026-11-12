namespace Faixa.Servicos;

/// <summary>
/// Grava artista, música, álbum, ano e número da faixa dentro do arquivo. O yt-dlp já deixa a capa
/// e os dados do vídeo; aqui eles são trocados pelos nomes limpos (ou editados na tela) e, nas músicas
/// do Spotify e do Deezer, pela capa oficial do álbum.
/// </summary>
public static class Etiquetas
{
    static Etiquetas()
    {
        // ID3v2.3: a versão que o Explorer e o Windows Media Player leem sem tropeçar.
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;
    }

    public static void Gravar(string arquivo, NomeMusica nome, int? faixa = null, byte[]? capa = null)
    {
        using var f = TagLib.File.Create(arquivo);
        if (capa is { Length: > 0 })
        {
            f.Tag.Pictures =
            [
                new TagLib.Picture(new TagLib.ByteVector(capa))
                {
                    Type = TagLib.PictureType.FrontCover,
                    MimeType = capa is [0x89, 0x50, ..] ? "image/png" : "image/jpeg",
                    Description = "",
                },
            ];
        }
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

    /// <summary>Tamanho da capa gravada, em bytes (0 sem capa). Usado nos testes.</summary>
    public static int TamanhoDaCapa(string arquivo)
    {
        using var f = TagLib.File.Create(arquivo);
        return f.Tag.Pictures.FirstOrDefault()?.Data.Count ?? 0;
    }

    /// <summary>Lê de volta (usado nos testes e para conferir o resultado).</summary>
    public static (string? Artista, string? Musica, string? Album, uint Faixa, int VersaoId3) Ler(string arquivo)
    {
        using var f = TagLib.File.Create(arquivo);
        var id3 = f.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
        return (f.Tag.FirstPerformer, f.Tag.Title, f.Tag.Album, f.Tag.Track, id3?.Version ?? 0);
    }
}
