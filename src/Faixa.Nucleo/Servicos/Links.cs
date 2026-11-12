using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Faixa.Servicos;

public enum Plataforma { YouTube, SoundCloud, Spotify, Deezer }

public enum TipoCatalogo { Faixa, Album, Playlist }

/// <summary>O que um link colado aponta. Cada serviço tem o seu tipo.</summary>
public abstract record Link;

/// <summary>Faixa ou set (playlist, álbum, EP) do SoundCloud. O áudio vem do próprio SoundCloud, pelo yt-dlp.</summary>
public sealed record LinkSoundCloud(string Url, bool EhSet) : Link;

/// <summary>
/// Música, álbum ou playlist do Spotify ou do Deezer. Esses serviços protegem o áudio, então os dados
/// (nome, artista, álbum, capa) vêm deles e o áudio vem da mesma música no YouTube Music.
/// </summary>
public sealed record LinkCatalogo(Plataforma Plataforma, TipoCatalogo Tipo, string Id) : Link;

/// <summary>Link curto (spotify.link, link.deezer.com, on.soundcloud.com): só abrindo para saber aonde leva.</summary>
public sealed record LinkCurto(Plataforma Plataforma, string Url) : Link;

/// <summary>Página de um serviço conhecido que o app não baixa (perfil, artista, podcast).</summary>
public sealed record LinkSemSuporte(Plataforma Plataforma, string Motivo) : Link;

/// <summary>Reconhece links do YouTube, SoundCloud, Spotify e Deezer, inclusive os curtos dos apps.</summary>
public static partial class Links
{
    public const string Aceitos = "YouTube, SoundCloud, Spotify e Deezer";

    public static string Nome(Plataforma plataforma) => plataforma switch
    {
        Plataforma.SoundCloud => "SoundCloud",
        Plataforma.Spotify => "Spotify",
        Plataforma.Deezer => "Deezer",
        _ => "YouTube",
    };

    // Primeiras partes de endereço do SoundCloud que são páginas do site, não perfis.
    private static readonly HashSet<string> PaginasSoundCloud =
    [
        "discover", "search", "stream", "you", "upload", "charts", "stations", "pages", "settings", "messages",
        "notifications", "jobs", "imprint", "terms-of-use", "people", "tags", "mobile", "signin", "signup",
        "logout", "feed", "home", "for-artists", "pro", "connect", "creators", "popular", "explore", "playlists",
    ];

    // Segunda parte de endereço que é uma aba do perfil, não uma faixa.
    private static readonly HashSet<string> AbasSoundCloud =
    [
        "tracks", "albums", "sets", "popular-tracks", "reposts", "likes", "spotlight", "comments",
        "followers", "following", "recommended",
    ];

    public static Link? Reconhecer(string? texto)
    {
        if (YouTube.ExtrairLink(texto) is { } youtube)
            return youtube;
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        texto = texto.Trim();
        var uriSpotify = UriSpotify().Match(texto);
        if (uriSpotify.Success)
            return Spotify(uriSpotify.Groups[1].Value, uriSpotify.Groups[2].Value);

        if (!texto.Contains("://"))
            texto = "https://" + texto;
        if (!Uri.TryCreate(texto, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;

        var host = uri.Host.ToLowerInvariant();
        foreach (var prefixo in new[] { "www.", "m." })
            if (host.StartsWith(prefixo))
                host = host[prefixo.Length..];
        var partes = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToArray();

        return host switch
        {
            "open.spotify.com" or "play.spotify.com" => DoSpotify(partes),
            "spotify.link" or "spoti.fi" when partes.Length > 0 => new LinkCurto(Plataforma.Spotify, uri.GetLeftPart(UriPartial.Path)),
            "deezer.com" => DoDeezer(partes),
            "link.deezer.com" or "deezer.page.link" or "dzr.page.link" when partes.Length > 0 =>
                new LinkCurto(Plataforma.Deezer, uri.GetLeftPart(UriPartial.Path)),
            "soundcloud.com" => DoSoundCloud(partes),
            "on.soundcloud.com" when partes.Length > 0 => new LinkCurto(Plataforma.SoundCloud, uri.GetLeftPart(UriPartial.Path)),
            "api.soundcloud.com" when partes is ["tracks", var id] && id.All(char.IsAsciiDigit) =>
                new LinkSoundCloud($"https://api.soundcloud.com/tracks/{id}", EhSet: false),
            _ => null,
        };
    }

    /// <summary>
    /// Abre o link curto e devolve aonde ele leva. Quase todos respondem com um redirecionamento;
    /// os que respondem com uma página têm o endereço de destino escrito nela.
    /// </summary>
    public static async Task<Link?> ResolverCurtoAsync(LinkCurto curto, CancellationToken ct)
    {
        try
        {
            using var resp = await Rede.Http.GetAsync(curto.Url, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (resp.RequestMessage?.RequestUri is { } final && Reconhecer(final.ToString()) is { } destino and not LinkCurto)
                return destino;
            return DestinoNaPagina(await resp.Content.ReadAsStringAsync(ct), curto.Plataforma);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw Rede.Falha(Nome(curto.Plataforma), e);
        }
    }

    /// <summary>O primeiro endereço do serviço citado na página (HTML ou JSON com barras escapadas).</summary>
    internal static Link? DestinoNaPagina(string pagina, Plataforma plataforma)
    {
        var texto = pagina.Replace(@"\/", "/").Replace("&amp;", "&");
        foreach (Match m in EnderecoNaPagina().Matches(texto))
        {
            if (Reconhecer(m.Value) is { } link and not LinkCurto and not LinkSemSuporte && PlataformaDe(link) == plataforma)
                return link;
        }
        return null;
    }

    public static Plataforma PlataformaDe(Link link) => link switch
    {
        LinkSoundCloud => Plataforma.SoundCloud,
        LinkCatalogo c => c.Plataforma,
        LinkCurto c => c.Plataforma,
        LinkSemSuporte s => s.Plataforma,
        _ => Plataforma.YouTube,
    };

    private static Link? DoSpotify(string[] partes)
    {
        // open.spotify.com/intl-pt/track/ID e open.spotify.com/embed/track/ID levam à mesma música.
        var resto = partes.SkipWhile(p => p == "embed" || Idioma().IsMatch(p)).ToArray();
        return resto.Length >= 2 ? Spotify(resto[0], resto[1]) : null;
    }

    private static Link? Spotify(string tipo, string id)
    {
        TipoCatalogo? t = tipo switch
        {
            "track" => TipoCatalogo.Faixa,
            "album" => TipoCatalogo.Album,
            "playlist" => TipoCatalogo.Playlist,
            _ => null,
        };
        if (t is { } valido)
            return IdSpotify().IsMatch(id) ? new LinkCatalogo(Plataforma.Spotify, valido, id) : null;

        return tipo switch
        {
            "artist" => new LinkSemSuporte(Plataforma.Spotify, "Link de artista não funciona. Abra uma música, um álbum ou uma playlist."),
            "episode" or "show" => new LinkSemSuporte(Plataforma.Spotify, "Podcasts do Spotify não podem ser baixados."),
            "collection" or "user" => new LinkSemSuporte(Plataforma.Spotify, "Essa página só abre com login no Spotify. Use o link de uma playlist pública."),
            _ => null,
        };
    }

    private static Link? DoDeezer(string[] partes)
    {
        // deezer.com/br/track/ID: o idioma é opcional.
        var resto = partes.Length > 0 && IdiomaDeezer().IsMatch(partes[0]) ? partes[1..] : partes;
        if (resto.Length < 2)
            return null;

        TipoCatalogo? tipo = resto[0] switch
        {
            "track" => TipoCatalogo.Faixa,
            "album" => TipoCatalogo.Album,
            "playlist" => TipoCatalogo.Playlist,
            _ => null,
        };
        if (tipo is { } valido)
            return resto[1].Length > 0 && resto[1].All(char.IsAsciiDigit) ? new LinkCatalogo(Plataforma.Deezer, valido, resto[1]) : null;

        return resto[0] switch
        {
            "artist" => new LinkSemSuporte(Plataforma.Deezer, "Link de artista não funciona. Abra uma música, um álbum ou uma playlist."),
            "show" or "episode" or "podcast" => new LinkSemSuporte(Plataforma.Deezer, "Podcasts do Deezer não podem ser baixados."),
            "profile" => new LinkSemSuporte(Plataforma.Deezer, "Link de perfil não funciona. Abra uma playlist pública."),
            _ => null,
        };
    }

    private static Link? DoSoundCloud(string[] partes)
    {
        if (partes.Length == 0 || PaginasSoundCloud.Contains(partes[0]))
            return null;
        var perfil = partes[0];

        // soundcloud.com/perfil/sets/nome, com o código de link privado opcional no fim.
        if (partes.Length >= 3 && partes[1] == "sets")
            return new LinkSoundCloud($"https://soundcloud.com/{perfil}/sets/{string.Join('/', partes[2..Math.Min(partes.Length, 4)])}", EhSet: true);

        if (partes.Length == 1 || AbasSoundCloud.Contains(partes[1]))
            return new LinkSemSuporte(Plataforma.SoundCloud, "Link de perfil não funciona. Abra uma faixa ou um set (playlist ou álbum).");

        // soundcloud.com/perfil/faixa e soundcloud.com/perfil/faixa/s-XXXX (faixa privada com link).
        if (partes.Length == 2 || (partes.Length == 3 && partes[2].StartsWith("s-", StringComparison.Ordinal)))
            return new LinkSoundCloud($"https://soundcloud.com/{string.Join('/', partes)}", EhSet: false);
        return null;
    }

    [GeneratedRegex(@"^spotify:(track|album|playlist|artist|episode|show):([A-Za-z0-9]+)$")]
    private static partial Regex UriSpotify();

    [GeneratedRegex("^[A-Za-z0-9]{22}$")]
    private static partial Regex IdSpotify();

    [GeneratedRegex("^intl-[a-z]{2}(?:-[a-z]{2})?$", RegexOptions.IgnoreCase)]
    private static partial Regex Idioma();

    [GeneratedRegex("^[a-z]{2}(?:-[a-z]{2})?$", RegexOptions.IgnoreCase)]
    private static partial Regex IdiomaDeezer();

    [GeneratedRegex(@"https?://(?:open\.spotify\.com|(?:www\.)?deezer\.com|(?:www\.|m\.)?soundcloud\.com)/[^\s""'<>\\]+", RegexOptions.IgnoreCase)]
    private static partial Regex EnderecoNaPagina();
}
