using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Faixa.Servicos;

/// <summary>
/// Lê música, álbum e playlist do Spotify pela página de incorporação pública (a mesma que sites usam
/// para pôr o player do Spotify numa página). Não precisa de conta nem de chave, e inclui as playlists
/// do próprio Spotify. Limite: playlists mostram até 100 faixas, e o ISRC não vem.
/// </summary>
public static partial class Spotify
{
    // Faixas de playlist que a incorporação mostra, no máximo.
    public const int LimitePlaylist = 100;

    public static async Task<InfoCatalogo> BuscarAsync(LinkCatalogo link, CancellationToken ct)
    {
        var tipo = link.Tipo switch { TipoCatalogo.Faixa => "track", TipoCatalogo.Album => "album", _ => "playlist" };
        string html;
        try
        {
            using var resp = await Rede.Http.GetAsync($"https://open.spotify.com/embed/{tipo}/{link.Id}", ct);
            if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                throw Catalogo.NaoEncontrado(Plataforma.Spotify, link.Tipo);
            resp.EnsureSuccessStatusCode();
            html = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw Rede.Falha("Spotify", e);
        }

        try
        {
            return Interpretar(html, link.Tipo) ?? throw Catalogo.NaoEncontrado(Plataforma.Spotify, link.Tipo);
        }
        catch (JsonException e)
        {
            throw Catalogo.Ilegivel(Plataforma.Spotify, e.Message);
        }
    }

    /// <summary>Lê os dados do __NEXT_DATA__ da página. Null se a página não tiver a música (link errado ou removido).</summary>
    public static InfoCatalogo? Interpretar(string html, TipoCatalogo tipo)
    {
        var m = DadosDaPagina().Match(html);
        if (!m.Success)
            return null;

        using var doc = JsonDocument.Parse(m.Groups[1].Value);
        if (Catalogo.Caminho(doc.RootElement, "props", "pageProps", "state", "data", "entity") is not { ValueKind: JsonValueKind.Object } e)
            return null;

        var titulo = (Catalogo.Texto(e, "name") ?? Catalogo.Texto(e, "title") ?? "").Trim();
        if (titulo.Length == 0)
            return null;
        var subtitulo = (Catalogo.Texto(e, "subtitle") ?? "").Trim();
        var ano = Catalogo.Ano(Catalogo.Texto(e, "releaseDate", "isoString"));
        var capa = MaiorImagem(e);

        if (tipo == TipoCatalogo.Faixa)
        {
            var artistas = Catalogo.Itens(Catalogo.Caminho(e, "artists"))
                .Select(a => Catalogo.Texto(a, "name")?.Trim())
                .OfType<string>().Where(a => a.Length > 0).ToList();
            var faixa = new FaixaCatalogo(titulo, artistas, Segundos(e), Ano: ano, CapaUrl: capa);
            return new InfoCatalogo(Plataforma.Spotify, tipo, titulo, faixa.ArtistasTexto, capa, [faixa]);
        }

        var album = tipo == TipoCatalogo.Album;
        var faixas = new List<FaixaCatalogo>();
        var lidas = 0;
        foreach (var item in Catalogo.Itens(Catalogo.Caminho(e, "trackList")))
        {
            lidas++;
            // Playlists podem ter episódios de podcast no meio; ficam de fora.
            if (Catalogo.Texto(item, "entityType") is { } tipoItem && tipoItem != "track")
                continue;
            var nome = (Catalogo.Texto(item, "title") ?? "").Trim();
            if (nome.Length == 0)
                continue;
            faixas.Add(new FaixaCatalogo(nome, Artistas(Catalogo.Texto(item, "subtitle")), Segundos(item),
                Album: album ? titulo : null, Ano: album ? ano : null, CapaUrl: album ? capa : null));
        }

        if (faixas.Count == 0)
            return null;
        var cortada = !album && lidas >= LimitePlaylist;
        return new InfoCatalogo(Plataforma.Spotify, tipo, titulo, subtitulo, capa, faixas, cortada);
    }

    /// <summary>
    /// "Blue Swede, Björn Skifs": o Spotify separa artistas com vírgula e espaço inquebrável, então
    /// "Earth, Wind &amp; Fire" (vírgula e espaço comum) continua sendo um artista só.
    /// </summary>
    internal static List<string> Artistas(string? texto) =>
        (texto ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static double? Segundos(JsonElement e) =>
        Catalogo.Numero(e, "duration") is double ms and > 0 ? Math.Round(ms / 1000.0, 1) : null;

    private static string? MaiorImagem(JsonElement e)
    {
        var imagens = Catalogo.Itens(Catalogo.Caminho(e, "visualIdentity", "image"))
            .Select(i => (Url: Catalogo.Texto(i, "url"), Largura: Catalogo.Numero(i, "maxWidth") ?? 0))
            .Where(i => i.Url is not null)
            .OrderByDescending(i => i.Largura)
            .Select(i => i.Url);
        var capaPlaylist = Catalogo.Itens(Catalogo.Caminho(e, "coverArt", "sources")).Select(s => Catalogo.Texto(s, "url"));
        return imagens.FirstOrDefault() ?? capaPlaylist.FirstOrDefault(u => u is not null);
    }

    [GeneratedRegex(@"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex DadosDaPagina();
}
