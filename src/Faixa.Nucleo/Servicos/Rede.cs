using System.Net.Http;

namespace Faixa.Servicos;

/// <summary>A conexão HTTP única do app (prévias, catálogos, busca, componentes e atualização).</summary>
public static class Rede
{
    /// <summary>Identifica-se pelo nome do programa: nenhum serviço usado aqui pede para fingir ser um navegador.</summary>
    public static readonly HttpClient Http = Criar();

    /// <summary>Baixa uma capa. Null se o endereço for vazio, falhar ou não parecer uma imagem.</summary>
    public static async Task<byte[]?> BaixarImagemAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        try
        {
            using var resp = await Http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length > 2048 ? bytes : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException
                                  && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Erro de rede (sem internet, servidor fora do ar, tempo esgotado) traduzido para a tela.</summary>
    internal static ErroDownload Falha(string servico, Exception e) =>
        new(TipoErro.Conexao, $"Não consegui falar com o {servico}. Confira a internet e tente de novo.", e.Message);

    private static HttpClient Criar()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RDTrackDownloader/1.0 (+Windows)");
        return http;
    }
}
