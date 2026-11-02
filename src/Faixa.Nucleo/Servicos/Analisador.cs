using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Faixa.Servicos;

/// <summary>Um formato de áudio que o YouTube oferece para o vídeo (ex.: 251 = Opus, 140 = AAC).</summary>
public sealed record FormatoFonte(string Id, string Codec, double Kbps, int? Hz, long? Bytes)
{
    public string Nome => Analisador.NomeDoCodec(Codec);

    public string KbpsTexto => $"{Math.Round(Kbps):0} kbps";

    public string? KhzTexto => Hz is int hz ? $"{(hz / 1000.0).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"))} kHz" : null;
}

/// <summary>O que o YouTube tem de áudio para um vídeo.</summary>
/// <param name="Melhor">O de maior taxa no idioma original; é o que o Faixa baixa para converter.</param>
/// <param name="Originais">O melhor de cada codec que dá para salvar sem conversão (Opus e AAC).</param>
public sealed record InfoAudio(double? Duracao, FormatoFonte Melhor, IReadOnlyList<FormatoFonte> Originais);

/// <summary>
/// Pergunta ao yt-dlp quais formatos de áudio existem (-J, sem baixar nada) e escolhe o melhor
/// com regras próprias. O download depois pede exatamente esse formato pelo ID, então o que a tela
/// mostra é o que é baixado.
/// </summary>
public static class Analisador
{
    public static async Task<InfoAudio> AnalisarAsync(string id, CancellationToken ct)
    {
        using var proc = new Process
        {
            StartInfo = Componentes.ProcessoYtDlp([.. Componentes.ArgumentosBase, "-J", "--", YouTube.UrlDoVideo(id)]),
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

        return Interpretar(json)
               ?? throw new ErroDownload(TipoErro.Indisponivel, "O YouTube não ofereceu áudio para esse vídeo.", "");
    }

    /// <summary>Lê o JSON do yt-dlp -J. Devolve null se não houver nenhum formato de áudio utilizável.</summary>
    public static InfoAudio? Interpretar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        if (!raiz.TryGetProperty("formats", out var formatos) || formatos.ValueKind != JsonValueKind.Array)
            return null;

        var candidatos = new List<(FormatoFonte Formato, double Idioma, double Qualidade)>();
        foreach (var f in formatos.EnumerateArray())
        {
            // Só áudio puro: os formatos com vídeo junto são de qualidade de áudio menor.
            if (Texto(f, "vcodec") != "none" || Texto(f, "acodec") is null or "none")
                continue;

            var id = Texto(f, "format_id") ?? "";
            var nota = Texto(f, "format_note") ?? "";
            // DRC = volume comprimido pelo YouTube; DAMAGED = arquivo com defeito; DRM = protegido.
            if (id.EndsWith("-drc", StringComparison.OrdinalIgnoreCase)
                || nota.Contains("DRC", StringComparison.Ordinal)
                || nota.Contains("DAMAGED", StringComparison.Ordinal)
                || Verdadeiro(f, "has_drm"))
                continue;

            var kbps = Numero(f, "abr") ?? Numero(f, "tbr");
            if (kbps is not > 0)
                continue;

            var bytes = Numero(f, "filesize") ?? Numero(f, "filesize_approx");
            var formato = new FormatoFonte(id, NormalizarCodec(Texto(f, "acodec")!), kbps.Value,
                Numero(f, "asr") is double hz ? (int)hz : null,
                bytes is double b ? (long)b : null);

            // Idioma: 10 = áudio original, 5 = padrão, -1 = outra dublagem, -10 = audiodescrição.
            // Qualidade: a faixa que o próprio YouTube atribui (baixa, média, alta).
            candidatos.Add((formato, Numero(f, "language_preference") ?? 0, Numero(f, "quality") ?? 0));
        }

        if (candidatos.Count == 0)
            return null;

        // Mesma ordem que o yt-dlp usa para "melhor áudio": idioma, faixa de qualidade do YouTube,
        // codec (Opus soa melhor que AAC na mesma taxa) e só então os kbps.
        var idioma = candidatos.Max(c => c.Idioma);
        var doIdioma = candidatos
            .Where(c => c.Idioma == idioma)
            .OrderByDescending(c => c.Qualidade)
            .ThenByDescending(c => PesoDoCodec(c.Formato.Codec))
            .ThenByDescending(c => c.Formato.Kbps)
            .Select(c => c.Formato)
            .ToList();

        var originais = doIdioma
            .Where(f => f.Codec is "opus" or "aac")
            .GroupBy(f => f.Codec) // mantém a ordem acima: o Opus vem antes do AAC
            .Select(g => g.First())
            .ToList();

        return new InfoAudio(Numero(raiz, "duration"), doIdioma[0], originais);
    }

    private static int PesoDoCodec(string codec) => codec switch
    {
        "flac" or "alac" => 6,
        "opus" => 5,
        "vorbis" => 4,
        "aac" => 3,
        "mp3" => 2,
        "ec-3" or "ac-3" => 1,
        _ => 0,
    };

    public static string NormalizarCodec(string acodec)
    {
        var c = acodec.Trim().ToLowerInvariant();
        return c.StartsWith("mp4a") ? "aac" : c.Split('.')[0];
    }

    public static string NomeDoCodec(string codec) => codec switch
    {
        "opus" => "Opus",
        "aac" => "AAC",
        "vorbis" => "Vorbis",
        "flac" => "FLAC",
        "mp3" => "MP3",
        "ec-3" => "Dolby E-AC-3",
        "ac-3" => "Dolby AC-3",
        _ => codec.ToUpperInvariant(),
    };

    private static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Numero(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static bool Verdadeiro(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.String);
}
