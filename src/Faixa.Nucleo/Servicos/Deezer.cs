using System.Net.Http;
using System.Text.Json;

namespace Faixa.Servicos;

/// <summary>
/// Lê música, álbum e playlist pela API pública do Deezer (api.deezer.com), que não pede conta nem chave.
/// Traz o ISRC das músicas, o que ajuda a achar a gravação exata no YouTube Music.
/// </summary>
public static class Deezer
{
    private const string Api = "https://api.deezer.com/";

    // Para não ficar lendo para sempre uma lista enorme: 20 páginas de 100.
    private const int MaximoDeFaixas = 2000;

    public static async Task<InfoCatalogo> BuscarAsync(LinkCatalogo link, CancellationToken ct)
    {
        var tipo = link.Tipo switch { TipoCatalogo.Faixa => "track", TipoCatalogo.Album => "album", _ => "playlist" };
        var json = await PegarAsync($"{Api}{tipo}/{link.Id}", link.Tipo, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var info = Interpretar(doc.RootElement, link.Tipo) ?? throw Catalogo.NaoEncontrado(Plataforma.Deezer, link.Tipo);

            // Listas longas: a resposta traz só o começo; o resto vem em páginas do "tracklist".
            var total = (int)(Catalogo.Numero(doc.RootElement, "nb_tracks") ?? 0);
            var paginas = Catalogo.Texto(doc.RootElement, "tracklist");
            if (link.Tipo == TipoCatalogo.Faixa || paginas is null || info.Faixas.Count >= Math.Min(total, MaximoDeFaixas))
                return info;

            var faixas = info.Faixas.ToList();
            while (faixas.Count < Math.Min(total, MaximoDeFaixas))
            {
                var separador = paginas.Contains('?') ? '&' : '?';
                using var pagina = JsonDocument.Parse(await PegarAsync($"{paginas}{separador}index={faixas.Count}&limit=100", link.Tipo, ct));
                var ano = info.Faixas[0].Ano;
                var novas = Catalogo.Itens(Catalogo.Caminho(pagina.RootElement, "data"))
                    .Select(t => Faixa(t, info.Tipo == TipoCatalogo.Album ? info : null) is { } f ? f with { Ano = f.Ano ?? ano } : null)
                    .OfType<FaixaCatalogo>().ToList();
                if (novas.Count == 0)
                    break;
                faixas.AddRange(novas);
            }
            return info with { Faixas = faixas, Cortada = faixas.Count < total };
        }
        catch (JsonException e)
        {
            throw Catalogo.Ilegivel(Plataforma.Deezer, e.Message);
        }
    }

    public static InfoCatalogo? Interpretar(string json, TipoCatalogo tipo)
    {
        using var doc = JsonDocument.Parse(json);
        return Interpretar(doc.RootElement, tipo);
    }

    private static InfoCatalogo? Interpretar(JsonElement raiz, TipoCatalogo tipo)
    {
        // Link errado ou playlist privada: {"error": {"type": "DataException", "message": "no data", "code": 800}}
        if (raiz.ValueKind != JsonValueKind.Object || raiz.TryGetProperty("error", out _))
            return null;
        var titulo = (Catalogo.Texto(raiz, "title") ?? "").Trim();
        if (titulo.Length == 0)
            return null;

        switch (tipo)
        {
            case TipoCatalogo.Faixa:
            {
                var faixa = Faixa(raiz, null);
                return faixa is null ? null
                    : new InfoCatalogo(Plataforma.Deezer, tipo, faixa.Titulo, faixa.ArtistasTexto, faixa.CapaUrl, [faixa]);
            }

            case TipoCatalogo.Album:
            {
                var album = new InfoCatalogo(Plataforma.Deezer, tipo, titulo, (Catalogo.Texto(raiz, "artist", "name") ?? "").Trim(),
                    Capa(raiz), []);
                var ano = Catalogo.Ano(Catalogo.Texto(raiz, "release_date"));
                var faixas = Catalogo.Itens(Catalogo.Caminho(raiz, "tracks", "data"))
                    .Select(t => Faixa(t, album) is { } f ? f with { Ano = f.Ano ?? ano } : null)
                    .OfType<FaixaCatalogo>().ToList();
                return faixas.Count == 0 ? null : album with { Faixas = faixas };
            }

            default:
            {
                var faixas = Catalogo.Itens(Catalogo.Caminho(raiz, "tracks", "data"))
                    .Select(t => Faixa(t, null)).OfType<FaixaCatalogo>().ToList();
                return faixas.Count == 0 ? null
                    : new InfoCatalogo(Plataforma.Deezer, tipo, titulo, (Catalogo.Texto(raiz, "creator", "name") ?? "").Trim(),
                        Catalogo.Texto(raiz, "picture_xl") ?? Catalogo.Texto(raiz, "picture_big"), faixas);
            }
        }
    }

    /// <summary>Uma música da API. No álbum, as faixas não repetem o álbum, então ele vem de fora.</summary>
    private static FaixaCatalogo? Faixa(JsonElement t, InfoCatalogo? album)
    {
        var titulo = (Catalogo.Texto(t, "title") ?? "").Trim();
        if (titulo.Length == 0)
            return null;

        // Os "Main" de contributors são todos os artistas principais; sem eles, o artista da faixa.
        var artistas = Catalogo.Itens(Catalogo.Caminho(t, "contributors"))
            .Where(c => Catalogo.Texto(c, "role") is null or "Main")
            .Select(c => Catalogo.Texto(c, "name")?.Trim())
            .OfType<string>().Where(n => n.Length > 0).Distinct().ToList();
        if (artistas.Count == 0 && Catalogo.Texto(t, "artist", "name")?.Trim() is { Length: > 0 } artista)
            artistas.Add(artista);

        return new FaixaCatalogo(
            titulo,
            artistas,
            Catalogo.Numero(t, "duration") is double d and > 0 ? d : null,
            Album: album?.Titulo ?? Catalogo.Vazio(Catalogo.Texto(t, "album", "title")),
            Ano: Catalogo.Ano(Catalogo.Texto(t, "release_date") ?? Catalogo.Texto(t, "album", "release_date")),
            Isrc: Catalogo.Vazio(Catalogo.Texto(t, "isrc")),
            CapaUrl: album?.CapaUrl ?? (Catalogo.Caminho(t, "album") is { } a ? Capa(a) : null));
    }

    /// <summary>{"error": {"type": "Exception", "message": "Quota limit exceeded", "code": 4}}</summary>
    internal static bool LimiteDePedidos(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Catalogo.Numero(doc.RootElement, "error", "code") == 4;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Capa(JsonElement e) => Catalogo.Texto(e, "cover_xl") ?? Catalogo.Texto(e, "cover_big");

    private static async Task<string> PegarAsync(string url, TipoCatalogo tipo, CancellationToken ct)
    {
        try
        {
            using var resp = await Rede.Http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                throw Catalogo.NaoEncontrado(Plataforma.Deezer, tipo);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (LimiteDePedidos(json))
                throw new ErroDownload(TipoErro.Bloqueio, "O Deezer limitou os pedidos. Tente daqui a alguns segundos.", json);
            return json;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw Rede.Falha("Deezer", e);
        }
    }
}
