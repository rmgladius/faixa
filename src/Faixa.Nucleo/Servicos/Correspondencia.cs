using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Faixa.Servicos;

/// <summary>
/// Acha no YouTube Music a mesma gravação de uma música do Spotify ou do Deezer.
/// Compara nome (sem "(Remastered 2009)" e afins), artista e duração, e recusa versão ao vivo,
/// acústica, remix, cover ou karaokê quando a original não é. Sem um resultado confiável, não baixa nada.
/// </summary>
public static partial class Correspondencia
{
    /// <summary>Nota mínima (0 a 1) para aceitar um resultado.</summary>
    public const double NotaMinima = 0.7;

    // Diferença de duração acima da qual é outra gravação (outra edição, ao vivo, versão estendida).
    private const double DiferencaMaxima = 30;

    // Palavras que mudam a gravação: se aparecem de um lado e não do outro, não é a mesma.
    private static readonly HashSet<string> OutraGravacao =
    [
        "live", "vivo", "directo", "concert", "acoustic", "acustico", "acustica", "unplugged", "remix", "rmx",
        "instrumental", "karaoke", "cover", "tribute", "tributo", "sped", "slowed", "reverb", "nightcore", "8d",
        "demo", "orchestral", "piano",
    ];

    // Não contam na comparação de nomes.
    private static readonly HashSet<string> Vazias =
    [
        "the", "a", "an", "o", "os", "as", "de", "da", "do", "das", "dos", "e", "and", "y", "et", "&",
    ];

    /// <summary>
    /// Procura pelo ISRC (o código da gravação, quando o serviço informa) e, se não achar, por artista e nome.
    /// Devolve null quando nenhum resultado é confiável.
    /// </summary>
    public static async Task<ResultadoBusca?> ProcurarAsync(FaixaCatalogo faixa, CancellationToken ct)
    {
        if (faixa.Isrc is { Length: > 0 } isrc && Escolher(faixa, await YouTubeMusic.BuscarAsync(isrc, ct)) is { } exata)
            return exata;

        var artistas = string.Join(" ", faixa.Artistas.Take(2));
        return Escolher(faixa, await YouTubeMusic.BuscarAsync($"{artistas} {faixa.Titulo}".Trim(), ct));
    }

    /// <summary>O resultado de maior nota, se passar da mínima.</summary>
    public static ResultadoBusca? Escolher(FaixaCatalogo faixa, IReadOnlyList<ResultadoBusca> resultados) =>
        resultados
            .Select((r, i) => (Resultado: r, Nota: Nota(faixa, r, i)))
            .Where(x => x.Nota >= NotaMinima)
            .OrderByDescending(x => x.Nota)
            .Select(x => x.Resultado)
            .FirstOrDefault();

    /// <summary>De 0 (outra música) a ~1 (mesma gravação). A posição na busca desempata.</summary>
    public static double Nota(FaixaCatalogo faixa, ResultadoBusca r, int posicao = 1)
    {
        var titulo = Semelhanca(Palavras(Base(faixa.Titulo)), Palavras(Base(r.Titulo)));
        if (titulo < 0.5)
            return 0;

        var artista = Artista(faixa.Artistas, r.Artistas);
        if (artista == 0)
            return 0;

        double duracao;
        if (faixa.Duracao is double a && r.Duracao is double b)
        {
            var diferenca = Math.Abs(a - b);
            if (diferenca > DiferencaMaxima)
                return 0;
            duracao = diferenca switch { <= 3 => 1, <= 6 => 0.8, <= 10 => 0.6, <= 20 => 0.3, _ => 0.1 };
        }
        else
        {
            duracao = 0.5;
        }

        // Ao vivo, acústico, remix...: só vale se os dois forem.
        var palavrasOriginal = Palavras($"{faixa.Titulo} {faixa.Album}").ToHashSet();
        var palavrasAchada = Palavras($"{r.Titulo} {r.Album}").ToHashSet();
        var trocada = OutraGravacao.Any(p => palavrasOriginal.Contains(p) != palavrasAchada.Contains(p));

        // "(2022 Remaster)" contra "(Remastered 2009)": mesma música, edição diferente. Pesa pouco.
        var versaoOriginal = Palavras(Versao(faixa.Titulo)).ToHashSet();
        var versaoAchada = Palavras(Versao(r.Titulo)).ToHashSet();
        var diferentes = versaoOriginal.Count + versaoAchada.Count - 2 * versaoOriginal.Intersect(versaoAchada).Count();

        return 0.4 * titulo + 0.3 * artista + 0.3 * duracao
               - (trocada ? 0.4 : 0)
               - Math.Min(0.15, 0.05 * diferentes)
               + (posicao == 0 ? 0.03 : 0);
    }

    /// <summary>
    /// 1 se um artista do serviço aparece inteiro nos do YouTube Music ("Daft Punk" em "Daft Punk, Pharrell"),
    /// 0,5 se só o sobrenome coincide ("Tom Jobim" e "Antonio Carlos Jobim"), 0 se nada coincide.
    /// </summary>
    internal static double Artista(IReadOnlyList<string> artistas, string achados)
    {
        var alvo = Compacto(achados);
        if (alvo.Length == 0 || artistas.Count == 0)
            return 0;
        if (artistas.Select(Compacto).Any(a => a.Length >= 2 && (alvo.Contains(a) || a.Contains(alvo))))
            return 1;

        // Só o sobrenome conta: "Rick Astley" e "Rick Roll" dividem "rick", mas não são o mesmo artista.
        var palavras = Palavras(achados).Where(p => p.Length >= 4 && !Vazias.Contains(p)).ToHashSet();
        return artistas.Select(a => Palavras(a).LastOrDefault()).Any(p => p is { Length: >= 4 } && palavras.Contains(p)) ? 0.5 : 0;
    }

    /// <summary>
    /// O nome sem a versão: "Here Comes The Sun - Remastered 2009" e "Here Comes The Sun (Remastered 2009)"
    /// viram "Here Comes The Sun". Também tira "feat. Fulano".
    /// </summary>
    internal static string Base(string titulo)
    {
        var t = Parenteses().Replace(titulo, " ");
        t = SufixoTraco().Replace(t, "");
        t = Participacao().Replace(t, "");
        var limpo = t.Trim();
        return limpo.Length > 0 ? limpo : titulo;
    }

    /// <summary>O que diz a versão: o conteúdo dos parênteses e o que vem depois de " - ", sem as participações.</summary>
    internal static string Versao(string titulo)
    {
        var partes = Parenteses().Matches(titulo).Select(m => m.Groups[1].Value)
            .Concat(SufixoTraco().Matches(titulo).Select(m => m.Groups[1].Value))
            .Where(p => !ParticipacaoNaVersao().IsMatch(p));
        return string.Join(" ", partes);
    }

    private static double Semelhanca(List<string> a, List<string> b)
    {
        var x = a.Where(p => !Vazias.Contains(p)).ToHashSet();
        var y = b.Where(p => !Vazias.Contains(p)).ToHashSet();
        if (x.Count == 0 || y.Count == 0)
        {
            // Nomes feitos só de palavras comuns ("The The"): compara tudo.
            x = [.. a];
            y = [.. b];
        }
        if (x.Count == 0 || y.Count == 0)
            return 0;
        return 2.0 * x.Intersect(y).Count() / (x.Count + y.Count);
    }

    internal static List<string> Palavras(string texto) =>
        PalavraSolta().Matches(SemAcento(texto.ToLowerInvariant().Replace("&", " and "))).Select(m => m.Value).ToList();

    private static string Compacto(string texto) =>
        new(SemAcento(texto.ToLowerInvariant().Replace("&", "and")).Where(char.IsLetterOrDigit).ToArray());

    private static string SemAcento(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[\(\[]([^\(\)\[\]]*)[\)\]]")]
    private static partial Regex Parenteses();

    // " - Remastered 2009", " - Live", " - From ""Filme"""; só depois de um espaço, para não cortar "Jay-Z".
    [GeneratedRegex(@"\s[-–—]\s+(.+)$")]
    private static partial Regex SufixoTraco();

    // "Música feat. Fulano". Só feat/ft: "with" no meio do nome é parte dele ("Stay With Me").
    [GeneratedRegex(@"\s(?:feat\.?|ft\.?|featuring)\s.*$", RegexOptions.IgnoreCase)]
    private static partial Regex Participacao();

    // Parênteses que só dizem quem participa: "(feat. Fulano)", "(with Fulano)", "(part. Fulano)".
    [GeneratedRegex(@"^\s*(?:feat\.?|ft\.?|featuring|with|part\.?|participação|prod\.?)\s", RegexOptions.IgnoreCase)]
    private static partial Regex ParticipacaoNaVersao();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex PalavraSolta();
}
