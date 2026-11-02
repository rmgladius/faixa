using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Faixa.Servicos;

public sealed class Config
{
    public string? PastaDestino { get; set; }
    public DateTime UltimaAtualizacaoYtDlp { get; set; }

    /// <summary>Chave da última opção de formato escolhida (ex.: "mp3-320", "original-opus").</summary>
    public string? Formato { get; set; }

    private static string Arquivo => Path.Combine(Componentes.PastaApp, "config.json");

    public static string PastaMusicas => PastaDoSistema(Environment.SpecialFolder.MyMusic, "Music");

    public static string PastaDownloads
    {
        get
        {
            // Downloads não está no Environment.SpecialFolder; pede direto ao Windows (respeita pasta movida).
            try
            {
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var caminho) == 0
                    && !string.IsNullOrEmpty(caminho))
                    return caminho;
            }
            catch
            {
                // cai no caminho tradicional
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    /// <summary>
    /// Downloads › Músicas. Fica fora das pastas que o Acesso controlado a pastas do Windows
    /// protege (Documentos, Músicas, Imagens, Vídeos, Área de Trabalho), então nunca pede liberação.
    /// </summary>
    public static string PastaPadrao => Path.Combine(PastaDownloads, "Músicas");

    /// <summary>A pasta escolhida, ou a padrão se nada foi escolhido ou a escolhida sumiu.</summary>
    [JsonIgnore]
    public string PastaEfetiva =>
        !string.IsNullOrWhiteSpace(PastaDestino) && Directory.Exists(PastaDestino) ? PastaDestino : PastaPadrao;

    private static (Func<string> Caminho, string Nome)[] Conhecidas =>
    [
        (() => PastaMusicas, "Músicas"),
        (() => PastaDownloads, "Downloads"),
        (() => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documentos"),
        (() => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Área de Trabalho"),
        (() => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Vídeos"),
        (() => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Imagens"),
    ];

    /// <summary>
    /// Nome curto para mostrar na tela: "Músicas", "Downloads › Músicas", "Downloads › Músicas › Playlist",
    /// ou só o nome da própria pasta quando ela não fica perto de uma pasta conhecida do Windows.
    /// </summary>
    public static string NomeAmigavel(string pasta)
    {
        try
        {
            var completo = Normalizar(pasta);
            var conhecidas = Conhecidas.Select(c => (Caminho: Normalizar(c.Caminho()), c.Nome)).ToList();
            var abaixo = new List<string>();
            for (var atual = completo; atual is not null && abaixo.Count <= 2; atual = Path.GetDirectoryName(atual))
            {
                var conhecida = conhecidas.FirstOrDefault(c => Igual(c.Caminho, atual));
                if (conhecida.Nome is not null)
                    return string.Join(" › ", abaixo.Prepend(conhecida.Nome));
                abaixo.Insert(0, Path.GetFileName(atual));
            }
            var nome = Path.GetFileName(completo);
            return string.IsNullOrEmpty(nome) ? completo : nome;
        }
        catch
        {
            return pasta;
        }
    }

    private static string Normalizar(string pasta) => Path.GetFullPath(pasta).TrimEnd('\\');

    private static bool Igual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string PastaDoSistema(Environment.SpecialFolder pasta, string reserva)
    {
        var caminho = Environment.GetFolderPath(pasta);
        return string.IsNullOrEmpty(caminho)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), reserva)
            : caminho;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid pasta, uint opcoes, IntPtr token, out string caminho);

    public static Config Carregar()
    {
        try
        {
            if (File.Exists(Arquivo))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(Arquivo)) ?? new Config();
        }
        catch
        {
            // Arquivo corrompido: volta ao padrão.
        }
        return new Config();
    }

    public void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Componentes.PastaApp);
            File.WriteAllText(Arquivo, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Não salvar a preferência não impede o uso.
        }
    }
}
