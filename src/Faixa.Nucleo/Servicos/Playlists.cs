using System.Diagnostics;
using System.Text.Json;

namespace Faixa.Servicos;

/// <summary>Uma faixa da playlist, como o yt-dlp lista sem abrir cada vídeo.</summary>
/// <param name="Id">ID do vídeo no YouTube (vazio nas outras plataformas).</param>
/// <param name="Endereco">O que o yt-dlp baixa, quando não é um vídeo do YouTube (faixa do SoundCloud).</param>
/// <param name="Catalogo">Música do Spotify ou do Deezer: o áudio é procurado no YouTube Music na hora de baixar.</param>
/// <param name="Motivo">Por que não dá para baixar, quando <paramref name="Disponivel"/> é falso.</param>
public sealed record ItemLista(
    int Numero,
    string Id,
    string Titulo,
    string Canal,
    double? Duracao,
    bool Disponivel,
    string? Endereco = null,
    FaixaCatalogo? Catalogo = null,
    string? Motivo = null)
{
    public string Url => Endereco ?? YouTube.UrlDoVideo(Id);
}

/// <param name="Origem">De onde vem a lista. O áudio vem do YouTube, exceto no SoundCloud.</param>
/// <param name="CapaUrl">Capa da lista; sem ela, a do primeiro vídeo do YouTube.</param>
/// <param name="Cortada">O serviço mostrou só parte das faixas.</param>
public sealed record InfoLista(
    string Id,
    string Titulo,
    string Canal,
    bool EhAlbum,
    IReadOnlyList<ItemLista> Itens,
    Plataforma Origem = Plataforma.YouTube,
    string? CapaUrl = null,
    bool Cortada = false)
{
    public int Disponiveis => Itens.Count(i => i.Disponivel);

    public double DuracaoTotal => Itens.Where(i => i.Disponivel).Sum(i => i.Duracao ?? 0);
}

/// <summary>Lê o conteúdo de uma playlist ou álbum (--flat-playlist: só a lista, sem abrir cada vídeo).</summary>
public static class Playlists
{
    public static Task<InfoLista> ListarAsync(string listaId, CancellationToken ct) =>
        ListarAsync(YouTube.UrlDaLista(listaId), listaId, ct);

    public static async Task<InfoLista> ListarAsync(string url, string listaId, CancellationToken ct)
    {
        using var proc = new Process
        {
            StartInfo = Componentes.ProcessoYtDlp(
                [.. Componentes.ArgumentosBase, "--flat-playlist", "-J", "--", url]),
        };
        proc.Start();
        var saida = proc.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var erros = proc.StandardError.ReadToEndAsync(CancellationToken.None);

        await using (ct.Register(() => Componentes.Encerrar(proc)))
        {
            await proc.WaitForExitAsync(CancellationToken.None);
        }
        ct.ThrowIfCancellationRequested();

        var json = await saida;
        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
            throw Baixador.Traduzir((await erros).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var lista = Interpretar(json, listaId);
        if (lista is null || lista.Disponiveis == 0)
            throw new ErroDownload(TipoErro.Indisponivel, "Essa playlist está vazia ou só tem vídeos indisponíveis.", "");
        return lista;
    }

    public static InfoLista? Interpretar(string json, string listaId)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        if (!raiz.TryGetProperty("entries", out var entradas) || entradas.ValueKind != JsonValueKind.Array)
            return null;

        // Álbuns do YouTube Music: listas "OLAK5uy_...", páginas "MPREb..." e títulos "Album - Nome".
        var titulo = (Analisador.Texto(raiz, "title") ?? "Playlist").Trim();
        var ehAlbum = listaId.StartsWith("OLAK5uy_", StringComparison.Ordinal)
                      || listaId.StartsWith("MPREb", StringComparison.Ordinal)
                      || titulo.StartsWith("Album - ", StringComparison.Ordinal);
        if (titulo.StartsWith("Album - ", StringComparison.Ordinal))
            titulo = titulo["Album - ".Length..].Trim();

        var canalLista = Analisador.Texto(raiz, "channel") ?? Analisador.Texto(raiz, "uploader") ?? "";
        var itens = new List<ItemLista>();
        foreach (var e in entradas.EnumerateArray())
        {
            // Outros sites (SoundCloud): a entrada traz o endereço e, às vezes, nem o título.
            if (Analisador.Texto(e, "ie_key") is { } site && site != "Youtube" && Analisador.Texto(e, "url") is { } endereco)
            {
                itens.Add(new ItemLista(itens.Count + 1, "", Analisador.Texto(e, "title") ?? $"Faixa {itens.Count + 1}",
                    Analisador.Texto(e, "uploader") ?? "", Analisador.Numero(e, "duration"), true, Endereco: endereco));
                continue;
            }

            var id = Analisador.Texto(e, "id") ?? "";
            var tituloItem = Analisador.Texto(e, "title") ?? "";
            var disponivel = YouTube.ExtrairId(YouTube.UrlDoVideo(id)) == id
                             && tituloItem is not ("[Private video]" or "[Deleted video]" or "")
                             && Analisador.Texto(e, "availability") is not ("private" or "needs_auth" or "premium_only");
            itens.Add(new ItemLista(
                itens.Count + 1,
                id,
                tituloItem switch
                {
                    "[Private video]" => "Vídeo privado",
                    "[Deleted video]" => "Vídeo apagado",
                    "" => "Vídeo indisponível",
                    _ => tituloItem,
                },
                Analisador.Texto(e, "channel") ?? Analisador.Texto(e, "uploader") ?? "",
                Analisador.Numero(e, "duration"),
                disponivel));
        }

        return new InfoLista(listaId, titulo, Metadados.LimparCanal(canalLista), ehAlbum, itens);
    }
}
