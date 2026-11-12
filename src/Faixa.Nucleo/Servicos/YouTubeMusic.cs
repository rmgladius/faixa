using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Faixa.Servicos;

/// <summary>Uma música da busca do YouTube Music.</summary>
public sealed record ResultadoBusca(string VideoId, string Titulo, string Artistas, string? Album, double? Duracao)
{
    public string Url => YouTube.UrlDoVideo(VideoId);

    public string UrlMusic => $"https://music.youtube.com/watch?v={VideoId}";

    /// <summary>"One More Time (Radio Edit) · Daft Punk · 5:21", para mostrar o que foi achado.</summary>
    public string Descricao => string.Join(" · ", new[]
    {
        Titulo, Artistas.Length > 0 ? Artistas : null, Duracao is double d ? Formatos.Duracao(d) : null,
    }.Where(x => x is not null));
}

/// <summary>
/// Busca músicas no YouTube Music, no filtro "Músicas" (as faixas oficiais das gravadoras, sem clipes).
/// Usa a mesma busca que o site music.youtube.com faz, que devolve título, artistas, álbum e duração
/// de cada resultado, o que permite conferir se é a mesma gravação antes de baixar.
/// </summary>
public static partial class YouTubeMusic
{
    private const string Endereco = "https://music.youtube.com/youtubei/v1/search?prettyPrint=false";

    // Parâmetro do filtro "Músicas" da busca.
    private const string FiltroMusicas = "EgWKAQIIAWoMEA4QChADEAQQCRAF";

    // Versão do cliente web do YouTube Music. Se um dia a busca parar de aceitar, é só atualizar aqui.
    private const string VersaoCliente = "1.20251020.01.00";

    public static async Task<IReadOnlyList<ResultadoBusca>> BuscarAsync(string consulta, CancellationToken ct)
    {
        var corpo = new
        {
            context = new { client = new { clientName = "WEB_REMIX", clientVersion = VersaoCliente, hl = "pt", gl = "BR" } },
            query = consulta,
            @params = FiltroMusicas,
        };
        try
        {
            using var resp = await Rede.Http.PostAsJsonAsync(Endereco, corpo, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new ErroDownload(TipoErro.Desconhecido, "A busca do YouTube Music recusou o pedido.",
                    $"HTTP {(int)resp.StatusCode}: {json[..Math.Min(json.Length, 500)]}");
            return Interpretar(json);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw Rede.Falha("YouTube Music", e);
        }
        catch (JsonException e)
        {
            throw new ErroDownload(TipoErro.Desconhecido, "A busca do YouTube Music respondeu de um jeito inesperado.", e.Message);
        }
    }

    /// <summary>
    /// Lê os resultados. Cada um tem colunas: [título] e [artistas • álbum • duração]; os pedaços
    /// da segunda coluna vêm separados por " • " e o álbum é o pedaço que aponta para uma página de álbum.
    /// </summary>
    public static IReadOnlyList<ResultadoBusca> Interpretar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var resultados = new List<ResultadoBusca>();
        foreach (var aba in Catalogo.Itens(Catalogo.Caminho(doc.RootElement, "contents", "tabbedSearchResultsRenderer", "tabs")))
        foreach (var secao in Catalogo.Itens(Catalogo.Caminho(aba, "tabRenderer", "content", "sectionListRenderer", "contents")))
        foreach (var item in Catalogo.Itens(Catalogo.Caminho(secao, "musicShelfRenderer", "contents")))
        {
            if (Catalogo.Caminho(item, "musicResponsiveListItemRenderer") is not { } r)
                continue;
            var colunas = Catalogo.Itens(Catalogo.Caminho(r, "flexColumns"))
                .Select(c => Catalogo.Itens(Catalogo.Caminho(c, "musicResponsiveListItemFlexColumnRenderer", "text", "runs")).ToList())
                .ToList();
            var videoId = Catalogo.Texto(r, "playlistItemData", "videoId")
                          ?? colunas.FirstOrDefault()?.Select(x => Catalogo.Texto(x, "navigationEndpoint", "watchEndpoint", "videoId"))
                              .FirstOrDefault(v => v is not null);
            if (videoId is null || colunas.Count < 2)
                continue;

            var titulo = string.Concat(colunas[0].Select(x => Catalogo.Texto(x, "text"))).Trim();
            var pedacos = Pedacos(colunas[1]);
            var album = pedacos.FirstOrDefault(p => p.Album).Texto;
            var duracao = pedacos.Count > 0 && Tempo(pedacos[^1].Texto) is double d ? d : (double?)null;
            var artistas = pedacos.Count > 0 && !pedacos[0].Album && Tempo(pedacos[0].Texto) is null ? pedacos[0].Texto : "";
            if (titulo.Length > 0)
                resultados.Add(new ResultadoBusca(videoId, titulo, artistas, album, duracao));
        }
        return resultados;
    }

    private static List<(string Texto, bool Album)> Pedacos(List<JsonElement> runs)
    {
        var pedacos = new List<(string, bool)>();
        var texto = new System.Text.StringBuilder();
        var album = false;
        foreach (var run in runs)
        {
            var t = Catalogo.Texto(run, "text") ?? "";
            if (t.Trim() == "•")
            {
                pedacos.Add((texto.ToString().Trim(), album));
                texto.Clear();
                album = false;
                continue;
            }
            texto.Append(t);
            album |= Catalogo.Texto(run, "navigationEndpoint", "browseEndpoint", "browseEndpointContextSupportedConfigs",
                "browseEndpointContextMusicConfig", "pageType") == "MUSIC_PAGE_TYPE_ALBUM";
        }
        pedacos.Add((texto.ToString().Trim(), album));
        return pedacos.Where(p => p.Item1.Length > 0).ToList();
    }

    /// <summary>"3:34" ou "1:02:03" em segundos.</summary>
    internal static double? Tempo(string texto)
    {
        var m = Duracao().Match(texto.Trim());
        if (!m.Success)
            return null;
        var partes = m.Value.Split(':').Select(int.Parse).ToArray();
        return partes.Aggregate(0, (total, p) => total * 60 + p);
    }

    [GeneratedRegex(@"^\d{1,2}(?::\d{2}){1,2}$")]
    private static partial Regex Duracao();
}
