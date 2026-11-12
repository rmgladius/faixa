using System.Diagnostics;
using System.IO;

namespace Faixa.Servicos;

/// <summary>
/// Acesso controlado a pastas (proteção contra ransomware do Windows Defender). Quando ligado,
/// só apps permitidos gravam em Documentos, Músicas, Imagens etc. Programa novo e sem assinatura
/// digital, como este app, fica de fora. Por isso a pasta padrão é Downloads › Músicas.
/// </summary>
public static class AcessoControlado
{
    /// <summary>
    /// true se a proteção está bloqueando (modo 1). Ler a configuração não exige administrador.
    /// Na dúvida (não deu para ler), responde true, que é o caso mais provável quando
    /// o Windows recusa gravar numa pasta do usuário.
    /// </summary>
    public static async Task<bool> EstaLigadoAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "(Get-MpPreference).EnableControlledFolderAccess" })
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi)!;
            var saida = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await proc.WaitForExitAsync(limite.Token);
            var valor = (await saida).Trim();
            return valor.Length == 0 || valor == "1";
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// true se a pasta está dentro de uma das que o Windows protege por padrão.
    /// Pastas extras que a pessoa tenha adicionado à proteção não entram nesta conta.
    /// </summary>
    public static bool PastaProtegida(string pasta)
    {
        Environment.SpecialFolder[] protegidas =
        [
            Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Favorites,
            Environment.SpecialFolder.CommonDocuments, Environment.SpecialFolder.CommonMusic,
            Environment.SpecialFolder.CommonPictures, Environment.SpecialFolder.CommonVideos,
        ];
        try
        {
            var alvo = Path.GetFullPath(pasta).TrimEnd('\\') + "\\";
            return protegidas
                .Select(Environment.GetFolderPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Any(p => alvo.StartsWith(Path.GetFullPath(p).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
