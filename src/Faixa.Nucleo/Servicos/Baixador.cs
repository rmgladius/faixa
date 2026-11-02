using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Faixa.Servicos;

public enum Fase { Iniciando, Baixando, Convertendo, Finalizando }

public sealed record Progresso(Fase Fase, double? Fracao = null);

public enum TipoErro { Conexao, Indisponivel, Restrito, Bloqueio, Gravacao, Conversao, Desconhecido }

public sealed class ErroDownload(TipoErro tipo, string mensagem, string detalhe) : Exception(mensagem)
{
    public TipoErro Tipo { get; } = tipo;
    public string Detalhe { get; } = detalhe;

    /// <summary>Erros que uma versão nova do yt-dlp pode resolver.</summary>
    public bool ValeAtualizar => Tipo is TipoErro.Desconhecido or TipoErro.Bloqueio;
}

/// <summary>O arquivo pronto e o formato do YouTube que realmente foi baixado (lido do próprio yt-dlp).</summary>
/// <param name="Dados">Artista, faixa e álbum oficiais, se o YouTube informar; senão, título e canal.</param>
public sealed record Resultado(string Arquivo, string? FonteCodec, double? FonteKbps, DadosMusica? Dados = null);

/// <summary>Roda o yt-dlp para um vídeo e devolve o arquivo pronto.</summary>
public static class Baixador
{
    // Marcador das linhas que nós mesmos pedimos ao yt-dlp (via --progress-template e --print),
    // para não depender do texto normal dele, que muda entre versões.
    private const string Marca = "FAIXA|";

    // Corta a capa 16:9 do vídeo no quadrado central, que é o formato de capa de álbum
    // que os players de música esperam. Receita usada pela comunidade do yt-dlp.
    private const string CorteQuadrado =
        "ThumbnailsConvertor+FFmpeg_o:-c:v mjpeg -qmin 1 -q:v 2 " +
        "-vf crop=\"'if(gt(ih,iw),iw,ih)':'if(gt(iw,ih),ih,iw)'\"";

    public static IReadOnlyList<string> Argumentos(string id, OpcaoFormato opcao, InfoAudio? audio)
    {
        // Pede o formato exato que a análise mostrou; se ele sumir entre a análise e o download,
        // cai no melhor áudio, e a linha "fonte" abaixo conta o que veio de fato.
        var fonte = opcao.Original ?? audio?.Melhor;
        var args = new List<string>(Componentes.ArgumentosBase)
        {
            "-f", fonte is null ? "bestaudio/best" : $"{fonte.Id}/bestaudio/best",
            "-x",
        };

        if (opcao.SemConversao)
            // "best": o yt-dlp só copia o áudio (Opus vira .opus, AAC vira .m4a) e nunca reconverte,
            // nem quando o formato pedido some e ele cai em outro.
            args.AddRange(["--audio-format", "best"]);
        else
            // MP3 com taxa fixa (-b:a NNNk no FFmpeg).
            args.AddRange(["--audio-format", "mp3", "--audio-quality", $"{opcao.Kbps}K"]);

        args.AddRange(
        [
            // Título, artista, data e capa dentro do arquivo.
            "--embed-metadata",
            "--embed-thumbnail", "--convert-thumbnails", "jpg",
            "--ppa", CorteQuadrado,

            // Tudo fica na pasta interna do Faixa; quem leva o arquivo ao destino é o próprio Faixa
            // (ver Entregar). Assim o Acesso controlado a pastas só olharia para o Faixa.exe.
            "--no-mtime", "--windows-filenames",
            "-P", Componentes.PastaProntos,
            "-P", "temp:" + Componentes.PastaTemp,
            "-o", "%(title)s.%(ext)s",

            // Com --print, o yt-dlp manda o progresso para a saída de erro; por isso as duas são lidas.
            "--newline", "--progress", "--progress-delta", "0.25",
            "--progress-template", "download:" + Marca + "dl|%(progress._percent)s",
            "--progress-template", "postprocess:" + Marca + "pp|%(progress.postprocessor)s|%(progress.status)s",
            "--print", "before_dl:" + Marca + "info|%(.{format_id,acodec,abr,tbr,track,artist,artists,album,release_year,title,channel,uploader})j",
            "--print", "after_move:" + Marca + "arquivo|%(filepath)s",

            "--", YouTube.UrlDoVideo(id),
        ]);
        return args;
    }

    /// <summary>Baixa e converte. Devolve o arquivo pronto, ainda na pasta interna do Faixa.</summary>
    public static async Task<Resultado> BaixarAsync(string id, OpcaoFormato opcao, InfoAudio? audio,
        IProgress<Progresso> progresso, CancellationToken ct)
    {
        Directory.CreateDirectory(Componentes.PastaProntos);
        var psi = Componentes.ProcessoYtDlp(Argumentos(id, opcao, audio));

        string? arquivo = null;
        string? fonteCodec = null;
        double? fonteKbps = null;
        DadosMusica? dados = null;
        var fase = Fase.Iniciando;
        var trava = new object();
        var erros = new List<string>();

        void Avancar(Fase nova, double? fracao = null)
        {
            if (nova < fase)
                return;
            fase = nova;
            progresso.Report(new Progresso(nova, fracao));
        }

        // Linha nossa (FAIXA|...) vinda de qualquer uma das saídas. Devolve false se não for.
        bool Interpretar(string linha)
        {
            if (!linha.StartsWith(Marca))
                return false;

            var campos = linha[Marca.Length..].Split('|');
            lock (trava)
            {
                switch (campos[0])
                {
                    case "dl" when campos.Length > 1:
                        if (double.TryParse(campos[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                            Avancar(Fase.Baixando, Math.Clamp(pct / 100, 0, 1));
                        else
                            Avancar(Fase.Baixando);
                        break;

                    case "pp" when campos.Length > 2 && campos[2] == "started":
                        if (campos[1] == "ExtractAudio")
                            Avancar(Fase.Convertendo);
                        else if (campos[1] is "Metadata" or "EmbedThumbnail" && fase >= Fase.Convertendo)
                            Avancar(Fase.Finalizando);
                        break;

                    case "info":
                        try
                        {
                            using var json = JsonDocument.Parse(linha[(Marca.Length + "info|".Length)..]);
                            var raiz = json.RootElement;
                            var acodec = Analisador.Texto(raiz, "acodec");
                            fonteCodec = acodec is null or "none" ? null : Analisador.NormalizarCodec(acodec);
                            fonteKbps = Analisador.Numero(raiz, "abr") ?? Analisador.Numero(raiz, "tbr");
                            dados = Analisador.LerDados(raiz);
                        }
                        catch (JsonException)
                        {
                            // Sem essas informações o arquivo sai com o nome do vídeo; não é motivo para falhar.
                        }
                        break;

                    case "arquivo" when campos.Length > 1:
                        arquivo = linha[(Marca.Length + "arquivo|".Length)..].Trim();
                        break;
                }
            }
            return true;
        }

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } linha)
                Interpretar(linha);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data) || Interpretar(e.Data))
                return;
            lock (erros)
            {
                erros.Add(e.Data);
                if (erros.Count > 200)
                    erros.RemoveAt(0);
            }
        };

        progresso.Report(new Progresso(Fase.Iniciando));
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await using (ct.Register(() => Componentes.Encerrar(proc)))
        {
            await proc.WaitForExitAsync(CancellationToken.None);
            proc.WaitForExit(); // garante que as últimas linhas da saída já foram lidas
        }

        if (ct.IsCancellationRequested)
        {
            LimparTemporarios();
            ct.ThrowIfCancellationRequested();
        }

        if (proc.ExitCode != 0 || arquivo is null || !File.Exists(arquivo))
        {
            string[] copia;
            lock (erros) copia = [.. erros];
            throw Traduzir(copia);
        }

        return new Resultado(arquivo, fonteCodec, fonteKbps, dados);
    }

    /// <summary>
    /// Move o MP3 pronto para a pasta escolhida, substituindo um de mesmo nome.
    /// Falha com IOException/UnauthorizedAccessException se o Windows bloquear a pasta.
    /// </summary>
    public static string Entregar(string arquivoPronto, string pastaDestino, string? nomeArquivo = null)
    {
        Directory.CreateDirectory(pastaDestino);
        var destino = Path.Combine(pastaDestino, nomeArquivo ?? Path.GetFileName(arquivoPronto));
        File.Move(arquivoPronto, destino, overwrite: true);
        return destino;
    }

    /// <summary>Apaga sobras de downloads interrompidos. Os MP3 prontos ficam: a janela os entrega ao abrir.</summary>
    public static void LimparTemporarios()
    {
        try
        {
            if (!Directory.Exists(Componentes.PastaTemp))
                return;
            foreach (var f in Directory.EnumerateFiles(Componentes.PastaTemp))
                try { File.Delete(f); } catch { }
        }
        catch
        {
            // Sobras não atrapalham o próximo download.
        }
    }

    internal static ErroDownload Traduzir(string[] linhas)
    {
        var erro = linhas.LastOrDefault(l => l.StartsWith("ERROR:")) ?? linhas.LastOrDefault() ?? "";
        var detalhe = string.Join(Environment.NewLine, linhas.TakeLast(15));

        bool Tem(params string[] trechos) =>
            trechos.Any(t => erro.Contains(t, StringComparison.OrdinalIgnoreCase));

        if (Tem("not a bot", "HTTP Error 429", "too many requests"))
            return new(TipoErro.Bloqueio, "O YouTube pediu verificação anti-robô. Tente daqui a alguns minutos.", detalhe);
        if (Tem("confirm your age", "age-restricted", "inappropriate for some users"))
            return new(TipoErro.Restrito, "Vídeo com restrição de idade. O YouTube só libera com login.", detalhe);
        if (Tem("members-only", "join this channel", "available to this channel's members"))
            return new(TipoErro.Restrito, "Vídeo exclusivo para membros do canal.", detalhe);
        if (Tem("private video"))
            return new(TipoErro.Indisponivel, "Esse vídeo é privado.", detalhe);
        if (Tem("live event will begin", "premieres in", "is upcoming", "this live event"))
            return new(TipoErro.Indisponivel, "Essa transmissão ainda não começou.", detalhe);
        if (Tem("playlist does not exist", "playlist is private", "unviewable playlist"))
            return new(TipoErro.Indisponivel, "Essa playlist não existe ou é privada.", detalhe);
        if (Tem("video unavailable", "has been removed", "not available in your country", "copyright"))
            return new(TipoErro.Indisponivel, "Esse vídeo não está disponível.", detalhe);
        if (Tem("getaddrinfo", "failed to resolve", "timed out", "connection", "network is unreachable", "unable to download webpage"))
            return new(TipoErro.Conexao, "Falha de conexão. Confira a internet e tente de novo.", detalhe);
        if (Tem("permission denied", "winerror 5", "errno 13", "access is denied", "no space left"))
            return new(TipoErro.Gravacao, "Não consegui gravar na pasta de destino.", detalhe);
        if (Tem("postprocessing", "ffmpeg", "ffprobe", "conversion failed"))
            return new(TipoErro.Conversao, "O áudio baixou, mas a conversão para MP3 falhou.", detalhe);

        return new(TipoErro.Desconhecido, "Não deu para baixar essa música.", detalhe);
    }
}
