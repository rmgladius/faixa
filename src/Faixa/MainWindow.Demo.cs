using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Faixa.Servicos;

namespace Faixa;

/// <summary>
/// Faixa.exe --demo &lt;pasta&gt;: monta a animação do README quadro a quadro, com músicas e capas
/// fictícias (pasta\capas\capaN.jpg, geradas por docs/fonte/capas.py) e sem acessar a rede.
/// Grava NNN.png, popup.png e quadros.txt ("nome duração_ms marca"), lidos por docs/fonte/montar.py.
/// </summary>
public partial class MainWindow
{
    private const string JsonDemo = """
        {"id": "q8kZ3vLu0sA", "title": "Maré Alta - Luz de Cinema (Clipe Oficial) [4K]", "channel": "Maré Alta",
         "track": "Luz de Cinema", "artists": ["Maré Alta"], "album": "Horizonte", "release_year": 2026, "duration": 227,
         "formats": [
          {"format_id": "251", "vcodec": "none", "acodec": "opus", "abr": 141.2, "asr": 48000, "quality": 3, "filesize": 4010000},
          {"format_id": "140", "vcodec": "none", "acodec": "mp4a.40.2", "abr": 129.5, "asr": 44100, "quality": 3, "filesize": 3680000}
        ]}
        """;

    private const string JsonPlaylistDemo = """
        {"_type": "playlist", "id": "PLdomingodemanha", "title": "Domingo de Manhã", "uploader": "rmgladius", "entries": [
          {"id": "q8kZ3vLu0sA", "title": "Maré Alta - Luz de Cinema (Clipe Oficial) [4K]", "duration": 227, "channel": "Maré Alta"},
          {"id": "Fd2nQe81kLs", "title": "Os Gaviões do Cerrado - Estrada Velha (Ao Vivo)", "duration": 252, "channel": "Os Gaviões do Cerrado"},
          {"id": "uV7bX0aLm3Q", "title": "Lia Brandão \"Varanda\" (Visualizer)", "duration": 185, "channel": "Lia Brandão"},
          {"id": "c2TgH9wPz4E", "title": "Céu de Agosto - Coletivo Ventania", "duration": 321, "channel": "Coletivo Ventania"},
          {"id": "xxxxxxxxxx1", "title": "[Private video]", "duration": null},
          {"id": "Kp0sRq5JvYc", "title": "Duo Pitanga - Café Frio (Official Audio) #mpb", "duration": 178, "channel": "DuoPitangaVEVO"},
          {"id": "Wm3eT8yNb1D", "title": "Noel Siqueira - Último Bonde", "duration": 220, "channel": "Noel Siqueira"}
        ]}
        """;

    private async Task GerarDemoAsync()
    {
        var pasta = _pastaCapturas!;
        Directory.CreateDirectory(pasta);
        var quadros = new List<string>();

        async Task Quadro(int ms, string marca = "-")
        {
            var nome = $"{quadros.Count + 1:000}";
            await SalvarRaizAsync(Path.Combine(pasta, nome + ".png"));
            quadros.Add($"{nome} {ms} {marca}");
        }

        byte[] Capa(int i) => File.ReadAllBytes(Path.Combine(pasta, "capas", $"capa{i}.jpg"));

        void Link(string texto)
        {
            CampoLink.Text = texto;
            _espera.Stop(); // nada de prévia de verdade: a demonstração preenche tudo à mão
        }

        try
        {
            _prontos = true;
            AtualizarBotao();
            AtualizarStatus();
            await Quadro(1500, "vazio");

            // Uma música
            Link("https://youtu.be/q8kZ3vLu0sA");
            _link = new LinkYouTube("q8kZ3vLu0sA", null);
            MostrarCapaVazia("Carregando…");
            MostrarSemNome("Buscando o vídeo…");
            AtualizarBotao();
            await Quadro(450);

            _video = new InfoVideo("q8kZ3vLu0sA", "Maré Alta - Luz de Cinema (Clipe Oficial) [4K]", "Maré Alta", Capa(1));
            MostrarCapa(_video.Capa);
            DefinirNome(Metadados.Resolver(new DadosMusica(_video.Titulo, _video.Canal)));
            _analisando = true;
            MostrarFonte();
            AtualizarBotao();
            AtualizarStatus();
            await Quadro(1200, "previa");

            _audio = Analisador.Interpretar(JsonDemo)!;
            _analisando = false;
            DefinirNome(Metadados.Resolver(_audio.Dados!));
            MontarOpcoes();
            MostrarFonte();
            AtualizarBotao();
            AtualizarStatus();
            await Quadro(2400, "analise");

            await SalvarSeletorAbertoAsync(pasta);
            await Quadro(2000, "popup");

            _baixando = true;
            _opcaoEmAndamento = OpcaoAtual;
            AtualizarBotao();
            MostrarProgresso(new Progresso(Fase.Iniciando));
            PosicionarIndeterminada(0.15);
            await Quadro(350);
            foreach (var f in new[] { 0.06, 0.19, 0.34, 0.5, 0.66, 0.81, 0.94, 1.0 })
            {
                MostrarProgresso(new Progresso(Fase.Baixando, f));
                await Quadro(160);
            }
            MostrarProgresso(new Progresso(Fase.Convertendo));
            foreach (var x in new[] { 0.0, 0.3, 0.6, 0.9 })
            {
                PosicionarIndeterminada(x);
                await Quadro(210);
            }
            MostrarProgresso(new Progresso(Fase.Finalizando));
            PosicionarIndeterminada(0.5);
            await Quadro(450);

            _baixando = false;
            _baixada = true;
            _avisoResultado = new Aviso("Salva em Downloads › Músicas. "
                + DescreverResultado(OpcaoAtual, new Resultado("x.mp3", "opus", 141.2)) + ".", Tom.Sucesso, "Abrir pasta");
            AtualizarBotao();
            AtualizarStatus();
            await Quadro(3000, "pronto");

            // Playlist
            Link("https://www.youtube.com/playlist?list=PLdomingodemanha");
            _link = new LinkYouTube(null, "PLdomingodemanha");
            ZerarTudo();
            MostrarModoLista(true);
            ListaTipo.Text = "PLAYLIST";
            ListaTitulo.Text = "Lendo a playlist…";
            ListaResumo.Text = "";
            ListaCapa.Background = Pincel("Superficie2");
            ListaCapaIcone.Visibility = Visibility.Visible;
            _carregandoLista = true;
            AtualizarBotao();
            AtualizarStatus();
            await Quadro(650);

            _carregandoLista = false;
            MostrarLista(Playlists.Interpretar(JsonPlaylistDemo, "PLdomingodemanha")!);
            if (CarregarImagem(Capa(4)) is { } capaLista)
            {
                ListaCapa.Background = new ImageBrush(capaLista) { Stretch = Stretch.UniformToFill };
                ListaCapaIcone.Visibility = Visibility.Collapsed;
            }
            AtualizarSelecao();
            AtualizarStatus();
            await Quadro(2400, "playlist");

            _baixando = true;
            _opcaoEmAndamento = OpcaoAtual;
            var fila = _faixas.Where(f => f.Pendente).ToList();
            AtualizarSelecao();
            for (var i = 0; i < fila.Count; i++)
            {
                fila[i].Estado = EstadoFaixa.Baixando;
                ListaFaixas.ScrollIntoView(fila[i]);
                foreach (var f in new[] { 0.35, 0.8 })
                {
                    MostrarProgressoLista(fila[i], i, fila.Count, new Progresso(Fase.Baixando, f));
                    await Quadro(150, i == 2 && f > 0.5 ? "playlist-baixando" : "-");
                }
                fila[i].Estado = EstadoFaixa.Pronta;
                fila[i].Detalhe = "Opus 141 kbps → MP3 320 kbps";
            }

            _baixando = false;
            _avisoResultado = new Aviso($"{fila.Count} músicas salvas em Downloads › Músicas › Domingo de Manhã.", Tom.Sucesso, "Abrir pasta");
            ListaFaixas.ScrollIntoView(_faixas[0]);
            AtualizarSelecao();
            AtualizarStatus();
            await Quadro(3400, "fim");

            File.WriteAllLines(Path.Combine(pasta, "quadros.txt"), quadros);
        }
        catch (Exception erro)
        {
            File.WriteAllText(Path.Combine(pasta, "erro.txt"), erro.ToString());
        }
        finally
        {
            Close();
        }
    }

    /// <summary>Barra "indeterminada" parada numa posição (0 = entrando pela esquerda, 1 = saindo).</summary>
    private void PosicionarIndeterminada(double posicao)
    {
        BarraIndeterminada();
        var largura = Trilho.ActualWidth;
        Barra.Width = largura * 0.3;
        BarraDesloc.X = -largura * 0.3 + posicao * largura * 1.3;
    }

    private async Task SalvarRaizAsync(string arquivo, double escala = 2)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(Raiz.ActualWidth * escala), (int)Math.Ceiling(Raiz.ActualHeight * escala),
            96 * escala, 96 * escala, PixelFormats.Pbgra32);
        rtb.Render(Raiz);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(rtb));
        await using var saida = File.Create(arquivo);
        png.Save(saida);
    }

    /// <summary>
    /// A lista do seletor vive num Popup (outra janela), então sai num PNG à parte, com fundo
    /// transparente, e a posição dela em relação à janela vai para popup.txt.
    /// </summary>
    private async Task SalvarSeletorAbertoAsync(string pasta, double escala = 2)
    {
        var popup = (System.Windows.Controls.Primitives.Popup)SeletorFormato.Template.FindName("PART_Popup", SeletorFormato);
        SeletorFormato.IsDropDownOpen = true;
        await Task.Delay(400);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

        if (popup.Child is FrameworkElement lista)
        {
            double w = lista.ActualWidth, h = lista.ActualHeight;
            var desenho = new DrawingVisual();
            using (var dc = desenho.RenderOpen())
                dc.DrawRectangle(new VisualBrush(lista), null, new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * escala), (int)Math.Ceiling(h * escala),
                96 * escala, 96 * escala, PixelFormats.Pbgra32);
            rtb.Render(desenho);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(rtb));
            await using (var saida = File.Create(Path.Combine(pasta, "popup.png")))
                png.Save(saida);

            // Placement="Top", VerticalOffset="-6" e margem de 14 embaixo (espaço da sombra).
            var topoSeletor = SeletorFormato.TranslatePoint(new Point(0, 0), Raiz);
            var x = topoSeletor.X;
            var y = topoSeletor.Y - 6 - 14 - h;
            File.WriteAllText(Path.Combine(pasta, "popup.txt"), $"{x * escala:0} {y * escala:0}");
        }
        SeletorFormato.IsDropDownOpen = false;
    }
}
