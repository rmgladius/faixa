using System.Globalization;

namespace Faixa.Servicos;

/// <summary>Uma opção do seletor de formato.</summary>
public sealed class OpcaoFormato
{
    public required string Chave { get; init; }

    /// <summary>Linha principal na lista: "MP3 · 320 kbps", "Original · Opus 129 kbps".</summary>
    public required string Titulo { get; init; }

    /// <summary>O que aparece no seletor fechado: "MP3 · 320 kbps", "Opus original".</summary>
    public required string Curto { get; init; }

    public required string Detalhe { get; init; }

    public string Tamanho { get; init; } = "";

    /// <summary>Taxa do MP3 de saída; 0 nas opções sem conversão.</summary>
    public int Kbps { get; init; }

    /// <summary>Salva o áudio do YouTube como está (.opus ou .m4a), sem converter.</summary>
    public bool SemConversao { get; init; }

    /// <summary>O formato exato a pedir, quando a análise do vídeo já disse qual é. Null = o melhor que houver.</summary>
    public FormatoFonte? Original { get; init; }

    public string Extensao => !SemConversao ? "mp3" : Original?.Codec == "aac" ? "m4a" : "opus";
}

public static class Formatos
{
    public const string Padrao = "mp3-320";
    public const string OriginalGenerico = "original-melhor";

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly (int Kbps, string Detalhe)[] Mp3 =
    [
        (320, "Perde o mínimo na conversão"),
        (256, "Perde pouco na conversão"),
        (192, "Perda leve, arquivo menor"),
        (128, "Perda perceptível, menor arquivo"),
    ];

    /// <summary>
    /// Vídeo: MP3 em quatro taxas e, depois da análise, os originais de cada codec com a taxa exata.
    /// Playlist (<paramref name="duracaoLista"/> informado): MP3 com o tamanho total e um original genérico,
    /// porque a qualidade de cada faixa só se conhece ao baixá-la.
    /// </summary>
    public static List<OpcaoFormato> Montar(InfoAudio? audio, double? duracaoLista = null)
    {
        var playlist = duracaoLista is not null;
        var duracao = playlist ? duracaoLista : audio?.Duracao;

        var lista = Mp3.Select(m => new OpcaoFormato
        {
            Chave = $"mp3-{m.Kbps}",
            Titulo = $"MP3 · {m.Kbps} kbps",
            Curto = $"MP3 · {m.Kbps} kbps",
            Detalhe = m.Detalhe,
            Kbps = m.Kbps,
            Tamanho = Estimar(duracao, m.Kbps, playlist),
        }).ToList();

        if (playlist)
        {
            lista.Add(new OpcaoFormato
            {
                Chave = OriginalGenerico,
                Titulo = "Original · melhor do YouTube",
                Curto = "Original",
                Detalhe = "Sem conversão · .opus ou .m4a, o que houver",
                SemConversao = true,
            });
            return lista;
        }

        foreach (var f in audio?.Originais ?? [])
        {
            lista.Add(new OpcaoFormato
            {
                Chave = $"original-{f.Codec}",
                Titulo = $"Original · {f.Nome} {f.KbpsTexto}",
                Curto = $"{f.Nome} original",
                Detalhe = f.Codec == "opus"
                    ? "Sem conversão · .opus · menos compatível"
                    : "Sem conversão · .m4a · toca em quase tudo",
                SemConversao = true,
                Original = f,
                Tamanho = f.Bytes is long bytes ? Mb(bytes) : Estimar(audio!.Duracao, f.Kbps, false),
            });
        }
        return lista;
    }

    /// <summary>
    /// A opção salva, se existir agora. Um original que não está disponível cai em outro original;
    /// sem nenhum original, volta ao MP3 320.
    /// </summary>
    public static OpcaoFormato Escolher(IReadOnlyList<OpcaoFormato> opcoes, string? chave) =>
        opcoes.FirstOrDefault(o => o.Chave == chave)
        ?? (chave?.StartsWith("original-") == true ? opcoes.FirstOrDefault(o => o.SemConversao) : null)
        ?? opcoes.First(o => o.Chave == Padrao);

    /// <summary>A frase honesta sobre a escolha: o que o YouTube tem e o que a conversão faz com isso.</summary>
    public static string Explicar(OpcaoFormato opcao, InfoAudio audio)
    {
        if (opcao.SemConversao)
        {
            var o = opcao.Original ?? audio.Melhor;
            return $"Sem conversão: o arquivo .{opcao.Extensao} é o próprio áudio do YouTube, {o.Nome} {o.KbpsTexto}.";
        }

        var fonte = audio.Melhor;
        return opcao.Kbps > fonte.Kbps
            ? $"O YouTube tem {fonte.Nome} {fonte.KbpsTexto}. Um MP3 de {opcao.Kbps} kbps não fica melhor que isso, só perde menos na conversão."
            : $"O YouTube tem {fonte.Nome} {fonte.KbpsTexto}. Converter para MP3 de {opcao.Kbps} kbps perde um pouco de qualidade.";
    }

    /// <summary>Versão para playlist: a qualidade de cada faixa só aparece quando ela termina.</summary>
    public static string ExplicarLista(OpcaoFormato opcao) => opcao.SemConversao
        ? "Sem conversão: cada arquivo é o próprio áudio do YouTube (.opus ou .m4a)."
        : $"Cada faixa vira MP3 {opcao.Kbps} kbps. A qualidade de origem aparece nela ao terminar.";

    public static string Duracao(double segundos)
    {
        var t = TimeSpan.FromSeconds(Math.Round(segundos));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    /// <summary>"48 min", "1 h 12 min": duração total de uma playlist.</summary>
    public static string DuracaoLonga(double segundos)
    {
        var minutos = (int)Math.Round(segundos / 60);
        return minutos < 60 ? $"{Math.Max(minutos, 1)} min" : $"{minutos / 60} h {minutos % 60:00} min";
    }

    // MP3 de taxa fixa: duração × taxa, mais ~130 KB de capa e tags por arquivo.
    private static string Estimar(double? duracao, double kbps, bool total)
    {
        if (duracao is not (double d and > 0))
            return "";
        var texto = Mb((long)(d * kbps * 1000 / 8) + 130_000);
        return total ? texto + " no total" : texto;
    }

    // Mesma conta do Explorer do Windows (1 MB = 1024 × 1024 bytes); a partir de 1 GB, em GB.
    private static string Mb(long bytes) => bytes >= 1L << 30
        ? $"≈ {(bytes / (double)(1L << 30)).ToString("0.0", Br)} GB"
        : $"≈ {(bytes / 1048576.0).ToString("0.0", Br)} MB";
}
