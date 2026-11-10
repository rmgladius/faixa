using System.IO;
using System.Windows;
using System.Windows.Threading;
using Faixa.Servicos;

namespace Faixa;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += AoFalhar;

        // Numa atualização com o Faixa aberto, o .exe antigo é renomeado para .old; apaga a sobra.
        try
        {
            if (Environment.ProcessPath is { } exe && File.Exists(exe + ".old"))
                File.Delete(exe + ".old");
        }
        catch
        {
            // Ainda em uso; fica para a próxima.
        }

        // --capturas <pasta>: cada estado da tela em PNG. --demo <pasta>: quadros da animação do README.
        var modo = e.Args.Length >= 2 ? e.Args[0] : null;
        var pasta = modo is "--capturas" or "--demo" ? e.Args[1] : null;
        var janela = new MainWindow(pasta, demo: modo == "--demo");
        MainWindow = janela;
        janela.Show();
    }

    private static void AoFalhar(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Componentes.PastaApp);
            File.AppendAllText(Path.Combine(Componentes.PastaApp, "falhas.txt"),
                $"{DateTime.Now:dd/MM/yyyy HH:mm:ss}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // sem log, segue
        }

        MessageBox.Show("Algo deu errado, mas o Faixa continua aberto.\n\n" + e.Exception.Message,
            "Faixa", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
