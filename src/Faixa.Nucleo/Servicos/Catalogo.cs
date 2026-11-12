using System.Text.Json;

namespace Faixa.Servicos;

/// <summary>Uma música como o Spotify ou o Deezer descrevem: os dados oficiais, sem o áudio.</summary>
/// <param name="Duracao">Em segundos. É o que mais ajuda a achar a mesma gravação no YouTube Music.</param>
/// <param name="Isrc">Código internacional da gravação (o Deezer informa; o Spotify, fora do app, não).</param>
public sealed record FaixaCatalogo(
    string Titulo,
    IReadOnlyList<string> Artistas,
    double? Duracao,
    string? Album = null,
    int? Ano = null,
    string? Isrc = null,
    string? CapaUrl = null)
{
    public string ArtistasTexto => string.Join(", ", Artistas);
}

/// <summary>O que um link de catálogo tem: uma música, ou um álbum ou playlist com várias.</summary>
/// <param name="Autor">Artista do álbum, ou dono da playlist.</param>
/// <param name="Cortada">O serviço mostrou só parte da lista (fora do app, o Spotify mostra até 100 faixas).</param>
public sealed record InfoCatalogo(
    Plataforma Plataforma,
    TipoCatalogo Tipo,
    string Titulo,
    string Autor,
    string? CapaUrl,
    IReadOnlyList<FaixaCatalogo> Faixas,
    bool Cortada = false);

/// <summary>Busca os dados de um link do Spotify ou do Deezer e converte para o que a tela usa.</summary>
public static class Catalogo
{
    public static Task<InfoCatalogo> BuscarAsync(LinkCatalogo link, CancellationToken ct) => link.Plataforma switch
    {
        Plataforma.Spotify => Spotify.BuscarAsync(link, ct),
        Plataforma.Deezer => Deezer.BuscarAsync(link, ct),
        _ => throw new ArgumentException("Plataforma sem catálogo: " + link.Plataforma),
    };

    /// <summary>Nome oficial da música. O álbum de reserva vem do YouTube Music, quando o serviço não informa.</summary>
    public static NomeMusica Nome(FaixaCatalogo faixa, string? albumReserva = null) =>
        new(faixa.ArtistasTexto, faixa.Titulo, faixa.Album ?? Vazio(albumReserva), faixa.Ano, OrigemNome.Catalogo);

    /// <summary>Álbum ou playlist no formato das listas do YouTube: cada linha leva a faixa do catálogo.</summary>
    public static InfoLista ParaLista(InfoCatalogo info, string id) => new(
        id,
        info.Titulo,
        info.Autor,
        info.Tipo == TipoCatalogo.Album,
        info.Faixas.Select((f, i) => new ItemLista(i + 1, "", f.Titulo, f.ArtistasTexto, f.Duracao, true, Catalogo: f)).ToList(),
        info.Plataforma,
        info.CapaUrl,
        info.Cortada);

    internal static ErroDownload NaoEncontrado(Plataforma plataforma, TipoCatalogo tipo) => new(TipoErro.Indisponivel,
        tipo switch
        {
            TipoCatalogo.Faixa => $"Não achei essa música no {Links.Nome(plataforma)}. Confira o link.",
            TipoCatalogo.Album => $"Não achei esse álbum no {Links.Nome(plataforma)}. Confira o link.",
            _ => $"Não achei essa playlist no {Links.Nome(plataforma)}. Ela pode ser privada.",
        }, "");

    internal static ErroDownload Ilegivel(Plataforma plataforma, string detalhe) => new(TipoErro.Desconhecido,
        $"O {Links.Nome(plataforma)} respondeu de um jeito inesperado. Uma versão nova do programa deve resolver.", detalhe);

    internal static int? Ano(string? data) =>
        data is { Length: >= 4 } && int.TryParse(data[..4], out var ano) && ano > 1000 ? ano : null;

    internal static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    internal static JsonElement? Caminho(JsonElement e, params string[] nomes)
    {
        foreach (var nome in nomes)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(nome, out var filho))
                return null;
            e = filho;
        }
        return e;
    }

    /// <summary>Texto num caminho do JSON, sem exceção se algum nível faltar ou vier nulo.</summary>
    internal static string? Texto(JsonElement? e, params string[] caminho) =>
        (e is { } x ? Caminho(x, caminho) : null) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    internal static double? Numero(JsonElement? e, params string[] caminho) =>
        (e is { } x ? Caminho(x, caminho) : null) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;

    internal static bool? Logico(JsonElement? e, params string[] caminho) =>
        (e is { } x ? Caminho(x, caminho) : null) switch
        {
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            _ => null,
        };

    internal static IEnumerable<JsonElement> Itens(JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Array } lista ? lista.EnumerateArray() : [];
}
