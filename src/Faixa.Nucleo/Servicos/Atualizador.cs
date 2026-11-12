using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Faixa.Servicos;

/// <summary>Uma versão publicada mais nova que a que está rodando.</summary>
public sealed record VersaoNova(Version Versao, string Notas, string Download, long Tamanho, string? Sha256)
{
    public string Texto => $"{Versao.Major}.{Versao.Minor}" + (Versao.Build > 0 ? $".{Versao.Build}" : "");
}

/// <summary>
/// Procura versão nova no GitHub Releases, baixa e confere o SHA-256 antes de trocar o .exe.
/// Só olha a release marcada como "latest" (pré-lançamentos ficam de fora).
/// </summary>
public static class Atualizador
{
    /// <summary>Repositório (dono/nome) onde as versões são publicadas.</summary>
    public const string Repositorio = Programa.Repositorio;

    /// <summary>Nome do arquivo baixado: o .exe autossuficiente, que roda em qualquer Windows 10/11 de 64 bits.</summary>
    public const string Arquivo = Programa.Executavel;

    public static Version VersaoAtual => Normalizar(typeof(Atualizador).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static string VersaoAtualTexto => $"{VersaoAtual.Major}.{VersaoAtual.Minor}.{VersaoAtual.Build}";

    /// <summary>Null quando não há versão nova, o repositório não existe ou não deu para consultar.</summary>
    public static async Task<VersaoNova?> ProcurarAsync(CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repositorio}/releases/latest");
        pedido.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await YouTube.Http.SendAsync(pedido, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        var nova = Interpretar(await resp.Content.ReadAsStringAsync(ct));
        if (nova is null || nova.Versao <= VersaoAtual)
            return null;

        // Sem "digest" no asset (releases antigas do GitHub), procura um .sha256 ao lado.
        if (nova.Sha256 is null && nova.Download.Length > 0)
        {
            try
            {
                var texto = await YouTube.Http.GetStringAsync(nova.Download + ".sha256", ct);
                var hash = texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (hash is { Length: 64 })
                    nova = nova with { Sha256 = hash.ToLowerInvariant() };
            }
            catch (HttpRequestException)
            {
                // sem arquivo de conferência
            }
        }
        return nova;
    }

    /// <summary>
    /// Lê a resposta de /releases/latest. Prefere o .exe com o nome atual e aceita o antigo (Faixa.exe).
    /// Null se a release não tiver nenhum dos dois.
    /// </summary>
    public static VersaoNova? Interpretar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        if (LerVersao(Analisador.Texto(raiz, "tag_name")) is not { } versao)
            return null;
        if (!raiz.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var lista = assets.EnumerateArray().ToList();
        foreach (var nome in new[] { Arquivo, Programa.ExecutavelAntigo })
        foreach (var a in lista)
        {
            if (!string.Equals(Analisador.Texto(a, "name"), nome, StringComparison.OrdinalIgnoreCase))
                continue;
            var digest = Analisador.Texto(a, "digest");
            return new VersaoNova(
                versao,
                (Analisador.Texto(raiz, "body") ?? "").Trim(),
                Analisador.Texto(a, "browser_download_url") ?? "",
                (long)(Analisador.Numero(a, "size") ?? 0),
                digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..].ToLowerInvariant() : null);
        }
        return null;
    }

    /// <summary>"v1.2.0", "1.2", "v1.2.0-beta" → 1.2.0. Null se não for versão.</summary>
    public static Version? LerVersao(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var texto = tag.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(texto.Contains('.') ? texto : texto + ".0", out var v) ? Normalizar(v) : null;
    }

    /// <summary>Baixa o .exe novo para a pasta interna e confere o SHA-256. Devolve o caminho do arquivo.</summary>
    public static async Task<string> BaixarAsync(VersaoNova nova, IProgress<double> progresso, CancellationToken ct)
    {
        Directory.CreateDirectory(Componentes.PastaTemp);
        var destino = Path.Combine(Componentes.PastaTemp, $"{Path.GetFileNameWithoutExtension(Arquivo)}-{nova.Texto}.exe");
        var parte = destino + ".part";

        using (var resp = await YouTube.Http.GetAsync(nova.Download, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? nova.Tamanho;
            await using var origem = await resp.Content.ReadAsStreamAsync(ct);
            await using var arquivo = new FileStream(parte, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 16];
            long lidos = 0;
            int n;
            while ((n = await origem.ReadAsync(buffer, ct)) > 0)
            {
                await arquivo.WriteAsync(buffer.AsMemory(0, n), ct);
                lidos += n;
                if (total > 0)
                    progresso.Report((double)lidos / total);
            }
        }

        if (nova.Sha256 is { } esperado)
        {
            string obtido;
            await using (var arquivo = File.OpenRead(parte))
                obtido = Convert.ToHexStringLower(await SHA256.HashDataAsync(arquivo, ct));
            if (obtido != esperado)
            {
                File.Delete(parte);
                throw new InvalidDataException("O arquivo baixado não confere com a versão publicada (SHA-256 diferente).");
            }
        }

        File.Move(parte, destino, overwrite: true);
        return destino;
    }

    /// <summary>
    /// Troca o .exe em uso pelo novo: o Windows deixa renomear um programa aberto, então o atual vira
    /// ".old" (apagado na próxima abertura) e o novo entra no lugar. Devolve o caminho para reabrir.
    /// </summary>
    public static string Instalar(string novoExe)
    {
        var atual = Environment.ProcessPath ?? throw new InvalidOperationException("Não sei onde o programa está instalado.");
        var antigo = atual + ".old";
        if (File.Exists(antigo))
            File.Delete(antigo);
        File.Move(atual, antigo);
        try
        {
            File.Move(novoExe, atual);
        }
        catch
        {
            File.Move(antigo, atual); // desfaz: continua na versão de antes
            throw;
        }
        return atual;
    }

    private static Version Normalizar(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
