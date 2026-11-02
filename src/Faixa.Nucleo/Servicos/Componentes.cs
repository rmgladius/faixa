using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace Faixa.Servicos;

/// <summary>
/// Cuida dos três programas que fazem o trabalho pesado, baixados na primeira vez que o Faixa abre:
/// yt-dlp (busca o áudio no YouTube), FFmpeg (converte para MP3 e grava a capa) e
/// Deno (o yt-dlp precisa dele desde 2025 para decifrar as assinaturas do YouTube).
/// </summary>
public static class Componentes
{
    public static readonly string PastaApp =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Faixa");

    public static string PastaBin => Path.Combine(PastaApp, "bin");
    public static string PastaTemp => Path.Combine(PastaApp, "temp");
    public static string PastaProntos => Path.Combine(PastaTemp, "prontos");

    public static string YtDlp => Path.Combine(PastaBin, "yt-dlp.exe");
    public static string Ffmpeg => Path.Combine(PastaBin, "ffmpeg.exe");
    public static string Ffprobe => Path.Combine(PastaBin, "ffprobe.exe");
    public static string Deno => Path.Combine(PastaBin, "deno.exe");

    /// <summary>Opções que toda chamada ao yt-dlp leva.</summary>
    public static IEnumerable<string> ArgumentosBase =>
    [
        "--ignore-config",
        "--no-playlist",
        "--color", "never",
        "--encoding", "utf-8",
        "--js-runtimes", "deno:" + Deno,
        "--ffmpeg-location", PastaBin,
    ];

    /// <summary>Prepara uma chamada ao yt-dlp com saída em UTF-8 e os componentes no PATH.</summary>
    public static ProcessStartInfo ProcessoYtDlp(IEnumerable<string> argumentos)
    {
        Directory.CreateDirectory(PastaTemp);
        var psi = new ProcessStartInfo(YtDlp)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = PastaTemp,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PATH"] = PastaBin + ";" + Environment.GetEnvironmentVariable("PATH");
        foreach (var arg in argumentos)
            psi.ArgumentList.Add(arg);
        return psi;
    }

    /// <summary>Encerra o processo e os filhos (ffmpeg, deno) sem reclamar se já tiver terminado.</summary>
    public static void Encerrar(Process processo)
    {
        try
        {
            if (!processo.HasExited)
                processo.Kill(entireProcessTree: true);
        }
        catch
        {
            // já terminou
        }
    }

    private sealed record Pacote(string Nome, string Url, long TamanhoAprox, Func<bool> Instalado, bool Zip, Func<string, bool> Extrair);

    private static readonly Pacote[] Pacotes =
    [
        new("yt-dlp",
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
            24_000_000, () => File.Exists(YtDlp), Zip: false, Extrair: _ => false),

        // Build do próprio projeto yt-dlp, com os ajustes que ele espera. A versão "shared"
        // tem metade do tamanho da estática (DLLs divididas entre ffmpeg e ffprobe).
        new("FFmpeg",
            "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip",
            90_000_000, () => File.Exists(Ffmpeg) && File.Exists(Ffprobe), Zip: true,
            Extrair: caminho => caminho.Contains("/bin/") && !caminho.EndsWith("/ffplay.exe")),

        new("Deno",
            "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip",
            43_000_000, () => File.Exists(Deno), Zip: true,
            Extrair: caminho => Path.GetFileName(caminho) == "deno.exe"),
    ];

    public static bool Prontos => Pacotes.All(p => p.Instalado());

    public static long TamanhoPendente => Pacotes.Where(p => !p.Instalado()).Sum(p => p.TamanhoAprox);

    /// <summary>Baixa o que estiver faltando. O progresso vai de 0 a 1 somando todos os pacotes.</summary>
    public static async Task PrepararAsync(IProgress<double> progresso, CancellationToken ct)
    {
        var pendentes = Pacotes.Where(p => !p.Instalado()).ToList();
        if (pendentes.Count == 0)
            return;

        Directory.CreateDirectory(PastaBin);
        Directory.CreateDirectory(PastaTemp);

        double total = pendentes.Sum(p => p.TamanhoAprox);
        double concluido = 0;

        foreach (var pacote in pendentes)
        {
            var peso = pacote.TamanhoAprox;
            var feito = concluido;
            var parcial = new Repasse(f => progresso.Report(Math.Min(1, (feito + f * peso) / total)));

            var destinoDownload = pacote.Zip
                ? Path.Combine(PastaTemp, pacote.Nome + ".zip")
                : YtDlp + ".part";

            await BaixarArquivoAsync(pacote.Url, destinoDownload, parcial, ct);

            if (pacote.Zip)
            {
                await Task.Run(() => ExtrairZip(destinoDownload, pacote.Extrair), ct);
                File.Delete(destinoDownload);
            }
            else
            {
                File.Move(destinoDownload, YtDlp, overwrite: true);
            }

            concluido += peso;
            progresso.Report(concluido / total);
        }
    }

    /// <summary>
    /// O YouTube muda com frequência e versões antigas do yt-dlp param de funcionar.
    /// O próprio yt-dlp.exe se atualiza com -U; aqui só rodamos e esperamos.
    /// </summary>
    public static async Task<bool> AtualizarYtDlpAsync(CancellationToken ct = default)
    {
        if (!File.Exists(YtDlp))
            return false;
        try
        {
            var psi = new ProcessStartInfo(YtDlp)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = PastaBin,
            };
            psi.ArgumentList.Add("-U");
            using var proc = Process.Start(psi)!;
            _ = proc.StandardOutput.ReadToEndAsync(ct);
            _ = proc.StandardError.ReadToEndAsync(ct);

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await proc.WaitForExitAsync(limite.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task BaixarArquivoAsync(string url, string destino, IProgress<double> progresso, CancellationToken ct)
    {
        var parte = destino + ".tmp";
        using (var resp = await YouTube.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var tamanho = resp.Content.Headers.ContentLength;

            await using var origem = await resp.Content.ReadAsStreamAsync(ct);
            await using var arquivo = new FileStream(parte, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

            var buffer = new byte[1 << 16];
            long lidos = 0;
            int n;
            while ((n = await origem.ReadAsync(buffer, ct)) > 0)
            {
                await arquivo.WriteAsync(buffer.AsMemory(0, n), ct);
                lidos += n;
                if (tamanho > 0)
                    progresso.Report((double)lidos / tamanho.Value);
            }
        }
        File.Move(parte, destino, overwrite: true);
    }

    private static void ExtrairZip(string zip, Func<string, bool> filtro)
    {
        using var arquivo = ZipFile.OpenRead(zip);
        // Os .exe por último: se a extração for interrompida, o pacote não parece instalado pela metade.
        foreach (var entrada in arquivo.Entries.OrderBy(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            var caminho = entrada.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(entrada.Name) || !filtro(caminho))
                continue;
            // Só o nome do arquivo: tudo vai solto em bin, sem recriar as pastas do zip.
            entrada.ExtractToFile(Path.Combine(PastaBin, entrada.Name), overwrite: true);
        }
    }

    private sealed class Repasse(Action<double> acao) : IProgress<double>
    {
        public void Report(double valor) => acao(valor);
    }
}
