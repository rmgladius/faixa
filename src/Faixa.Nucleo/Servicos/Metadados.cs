using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Faixa.Servicos;

/// <summary>De onde saíram o artista e o nome da música (mostrado na tela, por honestidade).</summary>
public enum OrigemNome
{
    /// <summary>Informados pelo YouTube: faixas do YouTube Music ou a seção "Música" de um vídeo.</summary>
    Oficial,

    /// <summary>Separados do título do vídeo ("Artista - Música").</summary>
    Titulo,

    /// <summary>Título do vídeo limpo, com o nome do canal como artista.</summary>
    Canal,

    /// <summary>Digitados por quem usa o app.</summary>
    Editado,

    /// <summary>Do Spotify ou do Deezer: nome, artistas e álbum oficiais da música.</summary>
    Catalogo,
}

/// <summary>O que o YouTube diz sobre o vídeo (oEmbed na prévia, yt-dlp na análise e no download).</summary>
public sealed record DadosMusica(
    string Titulo,
    string Canal,
    string? Faixa = null,
    string? Artistas = null,
    string? Album = null,
    int? Ano = null);

public sealed record NomeMusica(string Artista, string Musica, string? Album, int? Ano, OrigemNome Origem)
{
    public string Completo => string.IsNullOrWhiteSpace(Artista) ? Musica : $"{Artista} - {Musica}";
}

/// <summary>
/// Transforma "Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster)" do canal
/// "RickAstleyVEVO" em artista "Rick Astley" e música "Never Gonna Give You Up".
/// Dados oficiais do YouTube têm prioridade; sem eles, o título e o canal são interpretados.
/// </summary>
public static partial class Metadados
{
    // Palavras que, sozinhas num parêntese, indicam o tipo de vídeo e não fazem parte do nome.
    private static readonly HashSet<string> Ruido =
    [
        "official", "oficial", "video", "videoclipe", "videoclip", "clipe", "clip", "audio",
        "lyric", "lyrics", "letra", "legendado", "legendada", "traducao", "visualizer", "visualiser",
        "hd", "hq", "4k", "8k", "1080p", "720p", "2160p", "mv",
    ];

    // Podem acompanhar o ruído ("Official Music Video", "4K Remaster") sem mudar o sentido.
    // Sozinhas não bastam: "(Remastered 2009)" e "(Ao Vivo)" ficam, porque dizem qual versão é.
    private static readonly HashSet<string> Acompanhantes =
    [
        "music", "musica", "musical", "the", "o", "a", "de", "do", "da", "com", "with", "and", "e", "em", "in",
        "remaster", "remastered", "remasterizado", "version", "versao", "full", "new", "novo", "nova",
        "quality", "qualidade", "60fps", "upgrade", "com", "y", "con",
    ];

    public static NomeMusica Resolver(DadosMusica dados)
    {
        var album = Vazio(dados.Album);
        var faixa = Espacos(dados.Faixa ?? "");
        if (faixa.Length > 0)
        {
            var artistas = Espacos(dados.Artistas ?? "");
            return new NomeMusica(artistas.Length > 0 ? artistas : LimparCanal(dados.Canal), faixa, album, dados.Ano,
                OrigemNome.Oficial);
        }

        var titulo = LimparTitulo(dados.Titulo);
        var canal = LimparCanal(dados.Canal);
        return Separar(titulo, canal) is var (artista, musica)
            ? new NomeMusica(artista, musica, album, dados.Ano, OrigemNome.Titulo)
            : new NomeMusica(canal, titulo, album, dados.Ano, OrigemNome.Canal);
    }

    /// <summary>Tira "(Official Video)", "[4K]", "| Clipe Oficial", hashtags e afins do título.</summary>
    public static string LimparTitulo(string titulo)
    {
        var original = Espacos(titulo);
        var t = original.Replace('【', '[').Replace('】', ']');
        t = Hashtags().Replace(t, " ");
        t = Parenteses().Replace(t, m => EhRuido(m.Groups[1].Value) ? " " : m.Value);

        // Trechos finais depois de um separador: "Música - Clipe Oficial", "Música | Official Video".
        for (var m = TrechoFinal().Match(t); m.Success && EhRuido(m.Groups[2].Value); m = TrechoFinal().Match(t))
            t = m.Groups[1].Value;

        t = Espacos(SeparadorSolto().Replace(t, ""));
        return t.Length > 0 ? t : original;
    }

    /// <summary>"RickAstleyVEVO" → "Rick Astley"; "Tom Jobim - Topic" → "Tom Jobim".</summary>
    public static string LimparCanal(string canal)
    {
        var c = Espacos(canal);
        c = Topico().Replace(c, "");
        var vevo = Vevo().IsMatch(c);
        c = Vevo().Replace(c, "");
        c = Oficial().Replace(c, "");
        if (vevo && !c.Contains(' '))
            c = CamelCase().Replace(c, "$1 $2");
        return Espacos(c);
    }

    /// <summary>
    /// "Artista - Música" (ou "Música - Artista", quando o lado direito é o nome do canal)
    /// e 'Artista "Música"'. Null se o título não tiver separação clara.
    /// </summary>
    public static (string Artista, string Musica)? Separar(string titulo, string canal)
    {
        var partes = Traco().Split(titulo, 2);
        if (partes.Length == 2)
        {
            var esquerda = SemAspas(partes[0]);
            var direita = SemAspas(partes[1]);
            if (esquerda.Length > 0 && direita.Length > 0)
                return Parecido(direita, canal) && !Parecido(esquerda, canal)
                    ? (direita, esquerda)
                    : (esquerda, direita);
        }

        var aspas = Aspas().Match(titulo);
        if (aspas.Success)
        {
            var artista = Espacos(aspas.Groups["a"].Value);
            var musica = Espacos(aspas.Groups["m"].Value + aspas.Groups["r"].Value);
            if (artista.Length > 0 && musica.Length > 0)
                return (artista, musica);
        }
        return null;
    }

    /// <summary>"Artista - Música.mp3", com "01. " na frente quando há número de faixa.</summary>
    public static string NomeDeArquivo(NomeMusica nome, string extensao, int? numero = null)
    {
        var baseNome = numero is int n and > 0 ? $"{n:00}. {nome.Completo}" : nome.Completo;
        return $"{Sanitizar(baseNome)}.{extensao.TrimStart('.')}";
    }

    /// <summary>Troca o que o Windows não aceita em nome de arquivo e corta nomes enormes.</summary>
    public static string Sanitizar(string nome)
    {
        var sb = new StringBuilder(nome.Length);
        foreach (var ch in nome)
        {
            switch (ch)
            {
                case '/' or '\\' or '|':
                    sb.Append('-');
                    break;
                case ':':
                    sb.Append(" -");
                    break;
                case '"':
                    sb.Append('\'');
                    break;
                case '<' or '>' or '?' or '*':
                    break;
                default:
                    if (!char.IsControl(ch))
                        sb.Append(ch);
                    break;
            }
        }

        var limpo = Espacos(sb.ToString()).Trim('.', ' ');
        if (limpo.Length > 150)
            limpo = limpo[..150].TrimEnd('.', ' ');
        if (limpo.Length == 0)
            return "Música";
        return Reservados().IsMatch(limpo) ? limpo + "_" : limpo;
    }

    private static bool EhRuido(string texto)
    {
        var palavras = Palavras().Matches(SemAcento(texto.ToLowerInvariant())).Select(m => m.Value).ToList();
        return palavras.Count > 0
               && palavras.Any(Ruido.Contains)
               && palavras.All(p => Ruido.Contains(p) || Acompanhantes.Contains(p) || p.All(char.IsAsciiDigit));
    }

    private static bool Parecido(string a, string b)
    {
        var x = Compacto(a);
        var y = Compacto(b);
        return x.Length >= 3 && y.Length >= 3 && (x.Contains(y) || y.Contains(x));
    }

    private static string Compacto(string texto) =>
        new(SemAcento(texto.ToLowerInvariant()).Where(char.IsLetterOrDigit).ToArray());

    private static string SemAcento(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string SemAspas(string texto) => Espacos(texto).Trim('"', '“', '”');

    private static string Espacos(string texto) => MuitosEspacos().Replace(texto, " ").Trim();

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : Espacos(texto);

    [GeneratedRegex(@"\s+")]
    private static partial Regex MuitosEspacos();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])#[\p{L}\p{N}_]+")]
    private static partial Regex Hashtags();

    [GeneratedRegex(@"\s*[\(\[]([^\(\)\[\]]*)[\)\]]")]
    private static partial Regex Parenteses();

    // Último separador do título e o que vem depois dele.
    [GeneratedRegex(@"^(.*\S)\s+(?:\||//|[-–—])\s+(.+)$")]
    private static partial Regex TrechoFinal();

    [GeneratedRegex(@"\s*(?:\||//|[-–—])\s*$")]
    private static partial Regex SeparadorSolto();

    [GeneratedRegex(@"\s+[-–—]\s+")]
    private static partial Regex Traco();

    [GeneratedRegex(@"^(?<a>[^""“”]+?)\s+[""“](?<m>[^""“”]+)[""”](?<r>.*)$")]
    private static partial Regex Aspas();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Palavras();

    [GeneratedRegex(@"\s*-\s*Topic$", RegexOptions.IgnoreCase)]
    private static partial Regex Topico();

    [GeneratedRegex(@"\s*VEVO$", RegexOptions.IgnoreCase)]
    private static partial Regex Vevo();

    [GeneratedRegex(@"\s+(?:Official|Oficial)$", RegexOptions.IgnoreCase)]
    private static partial Regex Oficial();

    [GeneratedRegex(@"(\p{Ll})(\p{Lu})")]
    private static partial Regex CamelCase();

    [GeneratedRegex(@"^(?:CON|PRN|AUX|NUL|COM\d|LPT\d)(?:\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Reservados();
}
