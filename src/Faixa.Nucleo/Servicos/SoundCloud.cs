using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Faixa.Servicos;

/// <summary>
/// SoundCloud: o próprio yt-dlp baixa o áudio. Aqui ficam a prévia (oEmbed público, rápido como o do
/// YouTube) e a lista de um set, lida da própria página: o yt-dlp, sem abrir cada faixa, não traz os nomes.
/// </summary>
public static partial class SoundCloud
{
    public const string AvisoGoPlus = "Exclusiva do SoundCloud Go+: só 30 segundos estão liberados.";

    /// <summary>Título, artista e capa de uma faixa.</summary>
    public static async Task<InfoVideo> BuscarAsync(string url, CancellationToken ct)
    {
        var dados = await OEmbedAsync(url, ct) ?? throw new VideoNaoEncontradoException();
        var capa = await Rede.BaixarImagemAsync(dados.Capa, ct);
        return new InfoVideo(url, dados.Titulo, dados.Autor, capa, Endereco: url);
    }

    /// <summary>
    /// As faixas de um set. A página traz as cinco primeiras completas e só o código das outras;
    /// os nomes dessas vêm do oEmbed, cinco de cada vez.
    /// </summary>
    public static async Task<InfoLista> ListarSetAsync(string url, CancellationToken ct)
    {
        string html;
        try
        {
            using var resp = await Rede.Http.GetAsync(url, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new ErroDownload(TipoErro.Indisponivel, "Esse set não existe ou é privado.", "");
            resp.EnsureSuccessStatusCode();
            html = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw Rede.Falha("SoundCloud", e);
        }

        InfoLista? lista;
        try
        {
            lista = InterpretarSet(html, url);
        }
        catch (JsonException)
        {
            lista = null;
        }

        // Sem os dados na página (o SoundCloud mudou o site?): a lista do yt-dlp, só com os endereços.
        lista ??= (await Playlists.ListarAsync(url, url, ct)) with { Origem = Plataforma.SoundCloud };

        var semNome = lista.Itens.Where(i => i.Titulo.Length == 0).ToList();
        if (semNome.Count == 0)
            return lista;

        using var vagas = new SemaphoreSlim(5);
        var completos = await Task.WhenAll(lista.Itens.Select(async item =>
        {
            if (item.Titulo.Length > 0)
                return item;
            await vagas.WaitAsync(ct);
            try
            {
                return await OEmbedAsync(item.Url, ct) is { } d
                    ? item with { Titulo = d.Titulo, Canal = item.Canal.Length > 0 ? item.Canal : d.Autor }
                    : item with { Titulo = $"Faixa {item.Numero}" };
            }
            catch (ErroDownload)
            {
                return item with { Titulo = $"Faixa {item.Numero}" };
            }
            finally
            {
                vagas.Release();
            }
        }));
        return lista with { Itens = completos };
    }

    /// <summary>Lê o set do JSON que a página do SoundCloud traz embutido (window.__sc_hydration).</summary>
    public static InfoLista? InterpretarSet(string html, string url)
    {
        var m = DadosDaPagina().Match(html);
        if (!m.Success)
            return null;

        using var doc = JsonDocument.Parse(m.Groups[1].Value);
        var set = Catalogo.Itens(doc.RootElement)
            .Where(x => Catalogo.Texto(x, "hydratable") == "playlist")
            .Select(x => Catalogo.Caminho(x, "data"))
            .FirstOrDefault();
        if (set is not { } p || Catalogo.Texto(p, "title") is not { Length: > 0 } titulo)
            return null;

        var dono = Catalogo.Texto(p, "user", "username") ?? "";
        var tipo = Catalogo.Texto(p, "set_type") ?? "";
        var album = Catalogo.Logico(p, "is_album") == true || tipo is "album" or "ep" or "single" or "compilation";

        var itens = new List<ItemLista>();
        foreach (var t in Catalogo.Itens(Catalogo.Caminho(p, "tracks")))
        {
            var numero = itens.Count + 1;
            var id = Catalogo.Numero(t, "id") is double n ? ((long)n).ToString() : Catalogo.Texto(t, "id");
            var endereco = Catalogo.Texto(t, "permalink_url") ?? (id is null ? null : $"https://api.soundcloud.com/tracks/{id}");
            if (endereco is null)
                continue;

            // ALLOW = liberada; SNIP = só um trecho de 30 s (SoundCloud Go+); BLOCK = bloqueada no país.
            var politica = Catalogo.Texto(t, "policy");
            var motivo = politica switch
            {
                "SNIP" => AvisoGoPlus,
                "BLOCK" => "O SoundCloud bloqueia essa faixa no seu país.",
                _ => null,
            };
            var artista = Catalogo.Vazio(Catalogo.Texto(t, "publisher_metadata", "artist")) ?? Catalogo.Texto(t, "user", "username") ?? "";
            var duracao = Catalogo.Numero(t, "full_duration") ?? Catalogo.Numero(t, "duration");
            itens.Add(new ItemLista(numero, "", (Catalogo.Texto(t, "title") ?? "").Trim(), artista,
                duracao is double ms and > 0 ? Math.Round(ms / 1000.0, 1) : null,
                motivo is null, Endereco: endereco, Motivo: motivo));
        }
        if (itens.Count == 0)
            return null;

        var capa = Catalogo.Texto(p, "artwork_url")
                   ?? Catalogo.Itens(Catalogo.Caminho(p, "tracks")).Select(t => Catalogo.Texto(t, "artwork_url")).FirstOrDefault(c => c is not null);
        return new InfoLista(url, titulo.Trim(), dono, album, itens, Plataforma.SoundCloud, CapaGrande(capa));
    }

    /// <summary>"…-large.jpg" (100 px) vira "…-t500x500.jpg", o tamanho que o próprio site usa.</summary>
    internal static string? CapaGrande(string? url) => url?.Replace("-large.", "-t500x500.");

    private sealed record DadosOEmbed(string Titulo, string Autor, string? Capa);

    /// <summary>
    /// O título do oEmbed vem como "Nome by Autor"; o " by Autor" sai. Null se a faixa não existir.
    /// </summary>
    private static async Task<DadosOEmbed?> OEmbedAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await Rede.Http.GetAsync("https://soundcloud.com/oembed?format=json&url=" + Uri.EscapeDataString(url), ct);
            if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                return null;
            resp.EnsureSuccessStatusCode();
            var o = await resp.Content.ReadFromJsonAsync<OEmbed>(ct);
            if (o?.Title is not { Length: > 0 } titulo)
                return null;
            var autor = (o.AuthorName ?? "").Trim();
            return new DadosOEmbed(TirarAutor(titulo, autor), autor, o.ThumbnailUrl);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw Rede.Falha("SoundCloud", e);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string TirarAutor(string titulo, string autor)
    {
        titulo = titulo.Trim();
        var sufixo = " by " + autor;
        return autor.Length > 0 && titulo.EndsWith(sufixo, StringComparison.Ordinal) && titulo.Length > sufixo.Length
            ? titulo[..^sufixo.Length].Trim()
            : titulo;
    }

    private sealed class OEmbed
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("author_name")] public string? AuthorName { get; set; }
        [JsonPropertyName("thumbnail_url")] public string? ThumbnailUrl { get; set; }
    }

    [GeneratedRegex(@"window\.__sc_hydration\s*=\s*(\[.*?\]);\s*</script>", RegexOptions.Singleline)]
    private static partial Regex DadosDaPagina();
}
