namespace Faixa.Testes;

/// <summary>Trechos no formato da saída do yt-dlp -J, com os campos que o Faixa lê.</summary>
internal static class ExemplosYtDlp
{
    /// <summary>
    /// Clipe comum: Opus 129,5 e AAC 129,6 na mesma faixa de qualidade (vence o Opus),
    /// uma versão DRC com taxa maior (deve ser ignorada) e formatos com vídeo.
    /// </summary>
    public const string Clipe = """
        {"id": "dQw4w9WgXcQ", "title": "Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster)",
         "channel": "Rick Astley", "uploader": "Rick Astley", "duration": 213, "formats": [
          {"format_id": "sb0", "vcodec": "none", "acodec": "none", "ext": "mhtml", "format_note": "storyboard"},
          {"format_id": "249", "vcodec": "none", "acodec": "opus", "abr": 49.8, "asr": 48000, "ext": "webm", "format_note": "low", "quality": 2, "language_preference": -1, "has_drm": false},
          {"format_id": "139", "vcodec": "none", "acodec": "mp4a.40.5", "abr": 48.8, "asr": 22050, "ext": "m4a", "format_note": "low", "quality": 2, "language_preference": -1, "has_drm": false},
          {"format_id": "140", "vcodec": "none", "acodec": "mp4a.40.2", "abr": 129.6, "tbr": 129.6, "asr": 44100, "ext": "m4a", "format_note": "medium", "quality": 3, "language_preference": -1, "filesize": 3449447, "has_drm": false},
          {"format_id": "251-drc", "vcodec": "none", "acodec": "opus", "abr": 131.2, "asr": 48000, "ext": "webm", "format_note": "medium, DRC", "quality": 2.5, "language_preference": -1, "has_drm": false},
          {"format_id": "251", "vcodec": "none", "acodec": "opus", "abr": 129.5, "tbr": 129.5, "asr": 48000, "ext": "webm", "format_note": "medium", "quality": 3, "language_preference": -1, "filesize": 3437753, "has_drm": false},
          {"format_id": "18", "vcodec": "avc1.42001E", "acodec": "mp4a.40.2", "tbr": 503.9, "ext": "mp4", "quality": 1},
          {"format_id": "137", "vcodec": "avc1.640028", "acodec": "none", "tbr": 4400.1, "ext": "mp4", "quality": 8}
        ]}
        """;

    /// <summary>Vídeo dublado: a faixa original (10) vence a dublagem (-1) mesmo com taxa menor.</summary>
    public const string Dublado = """
        {"id": "aaaaaaaaaaa", "title": "Video", "duration": 60, "formats": [
          {"format_id": "251-1", "vcodec": "none", "acodec": "opus", "abr": 160.0, "asr": 48000, "quality": 3, "language": "pt", "language_preference": -1},
          {"format_id": "251-0", "vcodec": "none", "acodec": "opus", "abr": 120.0, "asr": 48000, "quality": 3, "language": "en", "language_preference": 10},
          {"format_id": "140-0", "vcodec": "none", "acodec": "mp4a.40.2", "abr": 128.0, "asr": 44100, "quality": 3, "language": "en", "language_preference": 10}
        ]}
        """;

    /// <summary>Faixa do YouTube Music (canal "Tópico"): o yt-dlp traz artista, faixa e álbum oficiais.</summary>
    public const string YouTubeMusic = """
        {"id": "bbbbbbbbbbb", "title": "Garota de Ipanema", "channel": "Tom Jobim - Topic", "uploader": "Tom Jobim - Topic",
         "track": "Garota de Ipanema", "artists": ["Tom Jobim", "Vinicius de Moraes"], "artist": "Tom Jobim, Vinicius de Moraes",
         "album": "Getz/Gilberto", "release_year": 1964, "duration": 324, "formats": [
          {"format_id": "251", "vcodec": "none", "acodec": "opus", "abr": 141.0, "asr": 48000, "quality": 3}
        ]}
        """;

    public const string SemAudio = """
        {"id": "ccccccccccc", "formats": [
          {"format_id": "137", "vcodec": "avc1", "acodec": "none", "tbr": 4400.0}
        ]}
        """;

    /// <summary>Saída de --flat-playlist -J: uma faixa normal, uma privada e uma apagada.</summary>
    public const string Playlist = """
        {"_type": "playlist", "id": "PLabcdefghijkl", "title": "Clássicos dos 80", "uploader": "Fulano", "channel": "Fulano",
         "entries": [
          {"_type": "url", "id": "dQw4w9WgXcQ", "title": "Rick Astley - Never Gonna Give You Up (Official Video)", "duration": 213.0, "channel": "Rick Astley", "uploader": "Rick Astley"},
          {"_type": "url", "id": "xxxxxxxxxx1", "title": "[Private video]", "duration": null, "channel": null},
          {"_type": "url", "id": "fJ9rUzIMcZQ", "title": "Queen – Bohemian Rhapsody (Official Video Remastered)", "duration": 359.0, "channel": "Queen Official", "uploader": "Queen Official"},
          {"_type": "url", "id": "xxxxxxxxxx2", "title": "[Deleted video]", "duration": null}
        ]}
        """;

    /// <summary>Álbum do YouTube Music: título "Album - ...", faixas de um canal "Tópico".</summary>
    public const string Album = """
        {"_type": "playlist", "id": "OLAK5uy_abcdefghijklmnop", "title": "Album - Getz/Gilberto", "uploader": "Stan Getz - Topic",
         "entries": [
          {"_type": "url", "id": "aaaaaaaaaa1", "title": "The Girl from Ipanema", "duration": 324.0, "channel": "Stan Getz - Topic"},
          {"_type": "url", "id": "aaaaaaaaaa2", "title": "Doralice", "duration": 166.0, "channel": "Stan Getz - Topic"}
        ]}
        """;
}
