using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Faixa.Servicos;

namespace Faixa;

/// <summary>Quem fez o programa e como falar com ele, mostrados no painel Sobre.</summary>
public static class Sobre
{
    public const string Email = "rmdiasvieira@icloud.com";
    public const string GitHub = "https://github.com/rmgladius";
    public const string GitHubTexto = "github.com/rmgladius";
    public const string LinkedIn = "https://www.linkedin.com/in/rmdiasvieira/";
    public const string LinkedInTexto = "linkedin.com/in/rmdiasvieira";
    public const string Problemas = Programa.UrlRepositorio + "/issues";
}

public partial class MainWindow
{
    private DispatcherTimer? _emailCopiado;

    private bool SobreAberto => PainelSobre.Visibility == Visibility.Visible;

    private void Sobre_Click(object sender, RoutedEventArgs e) => AbrirSobre();

    private void FecharSobre_Click(object sender, RoutedEventArgs e) => FecharSobre();

    /// <summary>Clique no fundo escuro, fora do cartão, fecha.</summary>
    private void PainelSobre_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, PainelSobre))
            FecharSobre();
    }

    private void AbrirSobre()
    {
        SobreVersao.Text = _versaoNova is { } nova
            ? $"Versão {Atualizador.VersaoAtualTexto} · a {nova.Texto} já saiu"
            : $"Versão {Atualizador.VersaoAtualTexto}";
        PainelSobre.Visibility = Visibility.Visible;
        PainelSobre.Focus();
        if (Capturando)
            return;

        var duracao = TimeSpan.FromMilliseconds(170);
        PainelSobre.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duracao));
        CartaoSobreDesloc.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, duracao) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void FecharSobre()
    {
        PainelSobre.Visibility = Visibility.Collapsed;
        VoltarRotuloDoEmail();
        CampoLink.Focus();
    }

    /// <summary>GitHub, LinkedIn, código-fonte: o endereço vem no Tag do botão.</summary>
    private void Endereco_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string endereco })
            AbrirArquivo(endereco);
    }

    /// <summary>
    /// Copia em vez de abrir um "mailto:": muita gente usa o e-mail no navegador e não tem
    /// programa de e-mail configurado no Windows, e aí o clique não faria nada.
    /// </summary>
    private void CopiarEmail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Capturando) // as capturas não mexem na área de transferência de quem roda
                Clipboard.SetText(Sobre.Email);
        }
        catch
        {
            return; // área de transferência ocupada por outro programa
        }

        EmailRotulo.Text = "E-mail copiado";
        EmailRotulo.Foreground = Pincel("Sucesso");
        _emailCopiado ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _emailCopiado.Tick -= EmailCopiado_Tick;
        _emailCopiado.Tick += EmailCopiado_Tick;
        _emailCopiado.Stop();
        _emailCopiado.Start();
    }

    private void EmailCopiado_Tick(object? sender, EventArgs e) => VoltarRotuloDoEmail();

    private void VoltarRotuloDoEmail()
    {
        _emailCopiado?.Stop();
        EmailRotulo.Text = "E-mail";
        EmailRotulo.Foreground = Pincel("TextoApagado");
    }
}
