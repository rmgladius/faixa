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

    /// <summary>Formato do YouTube salvo como está. Null = converte para MP3.</summary>
    public FormatoFonte? Original { get; init; }

    public string Extensao => Original is null ? "mp3" : Original.Codec == "opus" ? "opus" : "m4a";
}

public static class Formatos
{
    public const string Padrao = "mp3-320";

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly (int Kbps, string Detalhe)[] Mp3 =
    [
        (320, "Perde o mínimo na conversão"),
        (256, "Perde pouco na conversão"),
        (192, "Perda leve, arquivo menor"),
        (128, "Perda perceptível, menor arquivo"),
    ];

    /// <summary>MP3 em quatro taxas, mais os originais sem conversão quando a análise já terminou.</summary>
    public static List<OpcaoFormato> Montar(InfoAudio? audio)
    {
        var lista = Mp3.Select(m => new OpcaoFormato
        {
            Chave = $"mp3-{m.Kbps}",
            Titulo = $"MP3 · {m.Kbps} kbps",
            Curto = $"MP3 · {m.Kbps} kbps",
            Detalhe = m.Detalhe,
            Kbps = m.Kbps,
            Tamanho = Estimar(audio?.Duracao, m.Kbps),
        }).ToList();

        if (audio is null)
            return lista;

        foreach (var f in audio.Originais)
        {
            lista.Add(new OpcaoFormato
            {
                Chave = $"original-{f.Codec}",
                Titulo = $"Original · {f.Nome} {f.KbpsTexto}",
                Curto = $"{f.Nome} original",
                Detalhe = f.Codec == "opus"
                    ? "Sem conversão · .opus · menos compatível"
                    : "Sem conversão · .m4a · toca em quase tudo",
                Original = f,
                Tamanho = f.Bytes is long bytes ? Mb(bytes) : Estimar(audio.Duracao, f.Kbps),
            });
        }
        return lista;
    }

    /// <summary>
    /// A opção salva, se existir para este vídeo. Um original de codec que o vídeo não tem
    /// cai no outro original; sem originais, volta ao MP3 320.
    /// </summary>
    public static OpcaoFormato Escolher(IReadOnlyList<OpcaoFormato> opcoes, string? chave) =>
        opcoes.FirstOrDefault(o => o.Chave == chave)
        ?? (chave?.StartsWith("original-") == true ? opcoes.FirstOrDefault(o => o.Original is not null) : null)
        ?? opcoes.First(o => o.Chave == Padrao);

    /// <summary>
    /// A frase honesta sobre a escolha: o que o YouTube tem e o que a conversão faz com isso.
    /// </summary>
    public static string Explicar(OpcaoFormato opcao, InfoAudio audio)
    {
        if (opcao.Original is { } o)
            return $"Sem conversão: o arquivo .{opcao.Extensao} é o próprio áudio do YouTube, {o.Nome} {o.KbpsTexto}.";

        var fonte = audio.Melhor;
        return opcao.Kbps > fonte.Kbps
            ? $"O YouTube tem {fonte.Nome} {fonte.KbpsTexto}. Um MP3 de {opcao.Kbps} kbps não fica melhor que isso, só perde menos na conversão."
            : $"O YouTube tem {fonte.Nome} {fonte.KbpsTexto}. Converter para MP3 de {opcao.Kbps} kbps perde um pouco de qualidade.";
    }

    public static string Duracao(double segundos)
    {
        var t = TimeSpan.FromSeconds(Math.Round(segundos));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // MP3 de taxa fixa: duração × taxa, mais ~130 KB de capa e tags.
    private static string Estimar(double? duracao, double kbps) =>
        duracao is double d and > 0 ? Mb((long)(d * kbps * 1000 / 8) + 130_000) : "";

    // Mesma conta do Explorer do Windows (1 MB = 1024 × 1024 bytes).
    private static string Mb(long bytes) => $"≈ {(bytes / 1048576.0).ToString("0.0", Br)} MB";
}
