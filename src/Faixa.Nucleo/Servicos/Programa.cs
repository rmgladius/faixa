namespace Faixa.Servicos;

/// <summary>O nome do programa e o que leva esse nome: o .exe, a pasta interna e o repositório.</summary>
public static class Programa
{
    public const string Nome = "RD Track Downloader";

    /// <summary>O .exe publicado nas releases. Sem espaços, para atalhos e scripts.</summary>
    public const string Executavel = "RDTrackDownloader.exe";

    /// <summary>Nome do .exe até a versão 1.1. As releases publicam uma cópia com ele para quem ainda não atualizou.</summary>
    public const string ExecutavelAntigo = "Faixa.exe";

    /// <summary>
    /// Repositório (dono/nome) do código e das versões. Até a 1.1 era rmgladius/faixa; o GitHub
    /// redireciona o endereço antigo, e é por ele que a 1.1 continua achando as versões novas.
    /// </summary>
    public const string Repositorio = "rmgladius/rd-track-downloader";

    public const string UrlRepositorio = "https://github.com/" + Repositorio;
}
