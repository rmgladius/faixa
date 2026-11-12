namespace Faixa.Testes;

/// <summary>
/// Respostas reais gravadas em 11/11/2026 e reduzidas aos campos que o Faixa lê (pasta Exemplos):
/// busca do YouTube Music, páginas de incorporação do Spotify, API do Deezer e página de set do SoundCloud.
/// </summary>
internal static class Exemplos
{
    public static string Ler(string nome) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Exemplos", nome));
}
