using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Faixa.Servicos;

public sealed record InfoVideo(string Id, string Titulo, string Canal, byte[]? Capa)
{
    public string Url => YouTube.UrlDoVideo(Id);
}

/// <summary>O que um link aponta: um vídeo, uma playlist, ou os dois (vídeo aberto dentro de uma playlist).</summary>
public sealed record LinkYouTube(string? VideoId, string? ListaId);

public sealed class VideoNaoEncontradoException() : Exception("Vídeo não encontrado");

/// <summary>
/// Reconhece links do YouTube e busca a prévia (título, canal e capa) sem precisar do yt-dlp:
/// o oEmbed público responde em menos de um segundo, bem mais rápido que extrair o vídeo inteiro.
/// </summary>
public static partial class YouTube
{
    internal static readonly HttpClient Http = CriarHttp();

    // Da maior para a menor. A maxres e a hq720 são 16:9 sem tarja; a hqdefault tem tarja preta
    // em cima e embaixo, por isso fica por último.
    private static readonly string[] Capas = ["maxresdefault", "hq720", "mqdefault", "hqdefault"];

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex IdValido();

    [GeneratedRegex("(?:^|&)v=([A-Za-z0-9_-]{11})")]
    private static partial Regex ParametroV();

    [GeneratedRegex("(?:^|&)list=([A-Za-z0-9_-]{12,})")]
    private static partial Regex ParametroLista();

    public static string UrlDoVideo(string id) => $"https://www.youtube.com/watch?v={id}";

    /// <summary>Endereço da playlist (ou da página de álbum do YouTube Music, "MPREb...").</summary>
    public static string UrlDaLista(string id) => id.StartsWith("MPREb", StringComparison.Ordinal)
        ? $"https://music.youtube.com/browse/{id}"
        : $"https://www.youtube.com/playlist?list={id}";

    /// <summary>Devolve o ID de 11 caracteres de qualquer formato de link do YouTube, ou null.</summary>
    public static string? ExtrairId(string? texto) => ExtrairLink(texto)?.VideoId;

    /// <summary>
    /// Vídeo e/ou playlist de um link. Null se não for do YouTube ou não tiver nenhum dos dois.
    /// Mixes automáticos ("RD...", sem fim) e listas que exigem login (Curtidas, Assistir mais tarde)
    /// são ignorados: o link vale só pelo vídeo.
    /// </summary>
    public static LinkYouTube? ExtrairLink(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        texto = texto.Trim();
        if (!texto.Contains("://"))
            texto = "https://" + texto;
        if (!Uri.TryCreate(texto, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.ToLowerInvariant();
        foreach (var prefixo in new[] { "www.", "m." })
            if (host.StartsWith(prefixo))
                host = host[prefixo.Length..];

        var partes = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var consulta = uri.Query.TrimStart('?');
        string? id = null;
        string? lista = null;

        if (host == "youtu.be")
        {
            id = partes.FirstOrDefault();
            lista = ListaDaConsulta(consulta);
        }
        else if (host is "youtube.com" or "music.youtube.com" or "youtube-nocookie.com")
        {
            if (partes.Length > 0 && partes[0] == "watch")
            {
                var m = ParametroV().Match(consulta);
                id = m.Success ? m.Groups[1].Value : null;
                lista = ListaDaConsulta(consulta);
            }
            else if (partes.Length > 0 && partes[0] == "playlist")
            {
                lista = ListaDaConsulta(consulta);
            }
            else if (partes.Length > 1 && partes[0] == "browse" && partes[1].StartsWith("MPREb", StringComparison.Ordinal))
            {
                lista = partes[1];
            }
            else if (partes.Length > 1 && partes[0] is "shorts" or "live" or "embed" or "v")
            {
                id = partes[1];
            }
        }

        if (id is not null && !IdValido().IsMatch(id))
            id = null;
        return id is null && lista is null ? null : new LinkYouTube(id, lista);
    }

    private static string? ListaDaConsulta(string consulta)
    {
        var m = ParametroLista().Match(consulta);
        if (!m.Success)
            return null;
        var lista = m.Groups[1].Value;
        // RDCLAK5uy_ são playlists fixas do YouTube Music; os outros "RD" são mixes gerados na hora.
        // (Curtidas "LL" e Assistir mais tarde "WL" nem passam: o ID tem 2 letras e exige login.)
        var mix = lista.StartsWith("RD", StringComparison.Ordinal) && !lista.StartsWith("RDCLAK", StringComparison.Ordinal);
        return mix ? null : lista;
    }

    public static async Task<InfoVideo> BuscarAsync(string id, CancellationToken ct)
    {
        var tituloTask = BuscarTituloAsync(id, ct);
        var capaTask = BuscarCapaAsync(id, ct);
        var (titulo, canal) = await tituloTask;
        var capa = await capaTask;
        return new InfoVideo(id, titulo, canal, capa);
    }

    private static async Task<(string Titulo, string Canal)> BuscarTituloAsync(string id, CancellationToken ct)
    {
        var url = "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(UrlDoVideo(id));
        try
        {
            using var resp = await Http.GetAsync(url, ct);
            if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                throw new VideoNaoEncontradoException();
            if (resp.IsSuccessStatusCode)
            {
                var o = await resp.Content.ReadFromJsonAsync<OEmbed>(ct);
                if (!string.IsNullOrWhiteSpace(o?.Title))
                    return (o.Title.Trim(), LimparCanal(o.AuthorName));
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Sem prévia não é motivo para travar: o download ainda pode funcionar.
        }

        // 401 = vídeo com incorporação desativada. Dá para baixar, só não tem o título de antemão.
        return ("Vídeo do YouTube", "");
    }

    public static async Task<byte[]?> BuscarCapaAsync(string id, CancellationToken ct)
    {
        foreach (var nome in Capas)
        {
            try
            {
                using var resp = await Http.GetAsync($"https://i.ytimg.com/vi/{id}/{nome}.jpg", ct);
                if (!resp.IsSuccessStatusCode)
                    continue;
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length > 2048)
                    return bytes;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                return null;
            }
        }
        return null;
    }

    private static string LimparCanal(string? canal)
    {
        canal = (canal ?? "").Trim();
        return canal.EndsWith(" - Topic") ? canal[..^8] : canal;
    }

    private static HttpClient CriarHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Faixa/1.0 (+Windows)");
        return http;
    }

    private sealed class OEmbed
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("author_name")] public string? AuthorName { get; set; }
    }
}
