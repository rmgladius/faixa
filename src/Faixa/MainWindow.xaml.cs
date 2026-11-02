using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Faixa.Servicos;
using Microsoft.Win32;

namespace Faixa;

public partial class MainWindow : Window
{
    private enum Tom { Neutro, Sucesso, Erro }
    private enum ModoBarra { Oculta, Determinada, Indeterminada }

    /// <summary>Mensagem que fica na área de status quando nada está em andamento.</summary>
    private sealed record Aviso(string Texto, Tom Tom, string? Acao = null, Action? AoClicar = null, string? Detalhe = null);

    private const string IconeNota = "";
    private const string IconeAlerta = "";
    private const string IconeBaixar = "";
    private const string IconeOk = "";
    private const string IconeCancelar = "";

    private readonly Config _config = Config.Carregar();
    private readonly DispatcherTimer _espera;
    private readonly CancellationTokenSource _ctsJanela = new();
    private readonly string? _pastaCapturas;

    private InfoVideo? _video;
    private string? _idCarregando;
    private CancellationTokenSource? _ctsPrevia;
    private CancellationTokenSource? _ctsDownload;

    private bool _prontos;
    private bool _preparando;
    private bool _baixando;
    private bool _baixada;
    private double? _fracaoPreparo;
    private long _mbPreparo;
    private Task? _atualizacao;

    // Arquivo pronto que o Windows não deixou gravar no destino.
    private string? _pendente;
    private string? _pastaPendente;
    private string? _descricaoPendente;
    private string? _nomeArquivoPendente;

    // Nome da música: limpo do título ou oficial do YouTube; a pessoa pode editar.
    private NomeMusica? _nome;
    private bool _nomeEditado;
    private bool _definindoNome;

    // Link atual e modo playlist.
    private LinkYouTube? _link;
    private bool _modoLista;
    private InfoLista? _lista;
    private bool _carregandoLista;
    private bool _falhaLista;
    private CancellationTokenSource? _ctsLista;
    private readonly ObservableCollection<FaixaDaLista> _faixas = [];

    // Análise do áudio: os formatos que o YouTube oferece para o vídeo atual.
    private InfoAudio? _audio;
    private bool _analisando;
    private string? _falhaAnalise;
    private Aviso? _avisoAnalise;
    private CancellationTokenSource? _ctsAnalise;
    private List<OpcaoFormato> _opcoes = [];
    private bool _montandoOpcoes;
    private OpcaoFormato? _opcaoEmAndamento;

    private Aviso? _avisoLink;
    private Aviso? _avisoPreparo;
    private Aviso? _avisoResultado;
    private Action? _acaoStatus;

    private VersaoNova? _versaoNova;
    private bool _atualizando;

    private ModoBarra _modoBarra = ModoBarra.Oculta;
    private double _fracaoBarra;

    private bool Capturando => _pastaCapturas is not null;

    public MainWindow(string? pastaCapturas = null)
    {
        _pastaCapturas = pastaCapturas;
        InitializeComponent();

        _espera = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _espera.Tick += (_, _) =>
        {
            _espera.Stop();
            ProcessarLink();
        };

        ListaFaixas.ItemsSource = _faixas;
        Marca.ToolTip = $"Faixa {Atualizador.VersaoAtualTexto}";
        MontarOpcoes();

        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewDragOver += OnPreviewDragOver;
        PreviewDrop += OnPreviewDrop;
        Trilho.SizeChanged += (_, _) => ReajustarBarra();

        if (Capturando)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000;
            Top = 0;
            ShowActivated = false;
            ShowInTaskbar = false;
        }
    }

    // ───────────────────────── Início e fim ─────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AtualizarPasta();
        AtualizarBotao();

        if (Capturando)
        {
            await GerarCapturasAsync();
            return;
        }

        CampoLink.Focus();
        Baixador.LimparTemporarios();
        EntregarSobras();
        _ = ProcurarVersaoNovaAsync();
        await PrepararComponentesAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Barra de título escura e da mesma cor do fundo (Windows 11; no 10 só escurece).
        var hwnd = new WindowInteropHelper(this).Handle;
        var sim = 1;
        DwmSetWindowAttribute(hwnd, 20, ref sim, sizeof(int));
        var cor = 0x0015110F; // #0F1115 em COLORREF (0x00BBGGRR)
        DwmSetWindowAttribute(hwnd, 35, ref cor, sizeof(int));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_baixando && !Capturando)
        {
            var resposta = MessageBox.Show(this, "Uma música ainda está sendo baixada. Fechar mesmo assim?",
                "Faixa", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resposta != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _ctsDownload?.Cancel();
        }
        _ctsJanela.Cancel();
        base.OnClosing(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamanho);

    // ───────────────────────── Componentes (yt-dlp, FFmpeg, Deno) ─────────────────────────

    private async Task PrepararComponentesAsync()
    {
        if (Componentes.Prontos)
        {
            _prontos = true;
            if (DateTime.Now - _config.UltimaAtualizacaoYtDlp > TimeSpan.FromDays(2))
                _atualizacao = AtualizarYtDlpAsync();
            AtualizarBotao();
            AnalisarSePreciso();
            return;
        }

        _avisoPreparo = null;
        _preparando = true;
        _fracaoPreparo = 0;
        _mbPreparo = Componentes.TamanhoPendente / 1_000_000;
        AtualizarBotao();
        AtualizarStatus();

        try
        {
            await Componentes.PrepararAsync(new Progress<double>(f =>
            {
                if (_fracaoPreparo is null)
                    return;
                _fracaoPreparo = f;
                AtualizarStatus();
            }), _ctsJanela.Token);

            _prontos = true;
            _config.UltimaAtualizacaoYtDlp = DateTime.Now;
            _config.Salvar();
        }
        catch (OperationCanceledException) when (_ctsJanela.IsCancellationRequested)
        {
            return;
        }
        catch (Exception erro)
        {
            RegistrarErro("Preparo dos componentes", erro.ToString());
            _avisoPreparo = new Aviso("Não consegui baixar os componentes. Confira a internet.", Tom.Erro,
                "Tentar de novo", () => _ = PrepararComponentesAsync(), erro.Message);
        }
        finally
        {
            _preparando = false;
            _fracaoPreparo = null;
        }

        AtualizarBotao();
        AtualizarStatus();
        AnalisarSePreciso();
    }

    private async Task AtualizarYtDlpAsync()
    {
        await Componentes.AtualizarYtDlpAsync(_ctsJanela.Token);
        _config.UltimaAtualizacaoYtDlp = DateTime.Now;
        _config.Salvar();
    }

    // ───────────────────────── Link e prévia ─────────────────────────

    private void CampoLink_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var vazio = CampoLink.Text.Length == 0;
        Dica.Visibility = vazio ? Visibility.Visible : Visibility.Hidden;
        BotaoColar.Visibility = vazio ? Visibility.Visible : Visibility.Collapsed;
        BotaoLimpar.Visibility = vazio ? Visibility.Collapsed : Visibility.Visible;

        if (_baixando)
            return;
        _espera.Stop();
        _espera.Start();
    }

    private void CampoLink_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        _espera.Stop();
        ProcessarLink();
        if (BotaoBaixar.IsEnabled && !_baixando)
            _ = _modoLista ? BaixarListaAsync() : BaixarAsync();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+V em qualquer lugar da janela cola o link, mesmo sem clicar no campo.
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && !CampoLink.IsKeyboardFocusWithin && !_baixando)
        {
            Colar();
            e.Handled = true;
        }
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_baixando && e.Data.GetDataPresent(DataFormats.UnicodeText) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_baixando && e.Data.GetData(DataFormats.UnicodeText) is string texto && texto.Trim().Length > 0)
            DefinirLink(texto.Trim());
    }

    private void Colar_Click(object sender, RoutedEventArgs e) => Colar();

    private void Limpar_Click(object sender, RoutedEventArgs e)
    {
        CampoLink.Clear();
        _espera.Stop();
        ProcessarLink();
        CampoLink.Focus();
    }

    private void Colar()
    {
        string texto;
        try
        {
            texto = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
        }
        catch (COMException)
        {
            return; // área de transferência ocupada por outro programa
        }

        if (texto.Length > 0)
            DefinirLink(texto);
        CampoLink.Focus();
    }

    private void DefinirLink(string texto)
    {
        CampoLink.Text = texto;
        CampoLink.CaretIndex = texto.Length;
        _espera.Stop();
        ProcessarLink();
    }

    private void ProcessarLink()
    {
        var texto = CampoLink.Text.Trim();
        var link = YouTube.ExtrairLink(texto);

        // Mesmo link de antes: não recarrega nem apaga o "baixada".
        if (link is not null && link == _link)
            return;
        // Mesmo vídeo, só mudou o resto do link (ex.: "&t=30s" ou a playlist): atualiza a oferta de playlist.
        if (link?.VideoId is { } mesmo && !_modoLista && mesmo == (_video?.Id ?? _idCarregando))
        {
            _link = link;
            AtualizarStatus();
            return;
        }

        _link = link;
        ZerarTudo();

        if (texto.Length == 0)
        {
            _avisoLink = null;
            MostrarCapaVazia("A capa aparece aqui");
            MostrarSemNome("Nenhuma música ainda");
        }
        else if (link is null)
        {
            _avisoLink = new Aviso("Esse link não parece ser do YouTube.", Tom.Erro);
            MostrarCapaVazia("Link não reconhecido", IconeAlerta);
            MostrarSemNome("Nenhuma música ainda");
        }
        else if (link.VideoId is { } id)
        {
            _avisoLink = null;
            _ = CarregarPreviaAsync(id);
        }
        else
        {
            _avisoLink = null;
            _ = CarregarListaAsync(link.ListaId!);
        }

        AtualizarBotao();
        AtualizarStatus();
    }

    /// <summary>Esquece vídeo, análise e playlist (troca de link).</summary>
    private void ZerarTudo()
    {
        _ctsPrevia?.Cancel();
        _idCarregando = null;
        _video = null;
        _baixada = false;
        _pendente = null;
        _avisoResultado = null;
        _nome = null;
        _nomeEditado = false;

        _ctsLista?.Cancel();
        _ctsLista = null;
        _lista = null;
        _carregandoLista = false;
        _falhaLista = false;
        _faixas.Clear();
        MostrarModoLista(false);
        ZerarAnalise();
    }

    private async Task CarregarPreviaAsync(string id)
    {
        var cts = _ctsPrevia = new CancellationTokenSource();
        _idCarregando = id;
        MostrarCapaVazia("Carregando…", pulsando: true);
        MostrarSemNome("Buscando o vídeo…");

        try
        {
            var info = await YouTube.BuscarAsync(id, cts.Token);
            if (cts.IsCancellationRequested)
                return;
            _video = info;
            MostrarCapa(info.Capa);
            DefinirNome(Metadados.Resolver(new DadosMusica(info.Titulo, info.Canal)));
            AnalisarSePreciso();
        }
        catch (VideoNaoEncontradoException)
        {
            if (cts.IsCancellationRequested)
                return;
            _avisoLink = new Aviso("Vídeo não encontrado. Pode ser privado ou ter sido removido.", Tom.Erro);
            MostrarCapaVazia("Vídeo não encontrado", IconeAlerta);
            MostrarSemNome("Nenhuma música ainda");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            if (cts.IsCancellationRequested)
                return;
            // A prévia falhou, mas o download pode funcionar: deixa tentar.
            _video = new InfoVideo(id, "Vídeo do YouTube", "", null);
            MostrarCapa(null);
            DefinirNome(Metadados.Resolver(new DadosMusica(_video.Titulo, "")));
            AnalisarSePreciso();
        }
        finally
        {
            if (ReferenceEquals(_ctsPrevia, cts))
                _idCarregando = null;
        }

        AtualizarBotao();
        AtualizarStatus();
    }

    // ───────────────────────── Download ─────────────────────────

    private void Baixar_Click(object sender, RoutedEventArgs e)
    {
        if (_baixando)
        {
            BotaoBaixar.IsEnabled = false;
            BotaoTexto.Text = "Cancelando…";
            _ctsDownload?.Cancel();
            return;
        }
        _ = _modoLista ? BaixarListaAsync() : BaixarAsync();
    }

    private async Task BaixarAsync()
    {
        if (_video is not { } video || !_prontos || _baixando)
            return;

        _baixando = true;
        _baixada = false;
        _avisoResultado = null;
        var cts = _ctsDownload = new CancellationTokenSource();
        AtualizarBotao();

        var destino = _config.PastaEfetiva;
        var opcao = _opcaoEmAndamento = OpcaoAtual;
        var audio = _audio;
        var progresso = new Progress<Progresso>(MostrarProgresso);

        try
        {
            if (_atualizacao is { } atualizacao)
            {
                MostrarStatus("Atualizando o yt-dlp…");
                BarraIndeterminada();
                await atualizacao;
                _atualizacao = null;
            }
            cts.Token.ThrowIfCancellationRequested();

            Resultado pronto;
            try
            {
                pronto = await Baixador.BaixarAsync(video.Id, opcao, audio, progresso, cts.Token);
            }
            catch (ErroDownload erro) when (erro.ValeAtualizar && DateTime.Now - _config.UltimaAtualizacaoYtDlp > TimeSpan.FromHours(1))
            {
                // O YouTube muda e o yt-dlp corre atrás: atualiza e tenta mais uma vez.
                RegistrarErro("Primeira tentativa", erro.Detalhe);
                MostrarStatus("Atualizando o yt-dlp e tentando de novo…");
                BarraIndeterminada();
                await AtualizarYtDlpAsync();
                cts.Token.ThrowIfCancellationRequested();
                pronto = await Baixador.BaixarAsync(video.Id, opcao, audio, progresso, cts.Token);
            }

            MostrarStatus($"Salvando em {Config.NomeAmigavel(destino)}…");
            _pendente = pronto.Arquivo;
            _pastaPendente = destino;
            _descricaoPendente = DescreverResultado(opcao, pronto);

            // Nome: o editado na tela; senão, o que o YouTube informou no próprio download.
            var nome = _nomeEditado && _nome is not null
                ? _nome
                : pronto.Dados is { } dados ? Metadados.Resolver(dados) : _nome ?? Metadados.Resolver(new DadosMusica(video.Titulo, video.Canal));
            if (!_nomeEditado && nome != _nome)
                DefinirNome(nome);
            GravarEtiquetas(pronto.Arquivo, nome, null);
            _nomeArquivoPendente = Metadados.NomeDeArquivo(nome, Path.GetExtension(pronto.Arquivo).TrimStart('.'));
            await EntregarPendenteAsync();
        }
        catch (OperationCanceledException)
        {
            _avisoResultado = new Aviso("Download cancelado.", Tom.Neutro);
        }
        catch (ErroDownload erro)
        {
            var log = RegistrarErro(erro.Message, erro.Detalhe);
            var detalhes = erro.Tipo is TipoErro.Desconhecido or TipoErro.Conversao;
            _avisoResultado = new Aviso(erro.Message, Tom.Erro,
                detalhes ? "Detalhes" : null, detalhes ? () => AbrirArquivo(log) : null, erro.Detalhe);
        }
        catch (Exception erro)
        {
            var log = RegistrarErro("Falha ao rodar o yt-dlp", erro.ToString());
            _avisoResultado = new Aviso("Não consegui rodar o yt-dlp. O antivírus pode ter bloqueado.", Tom.Erro,
                "Detalhes", () => AbrirArquivo(log), erro.Message);
        }
        finally
        {
            _baixando = false;
            _ctsDownload = null;
            cts.Dispose();
        }

        AtualizarBotao();
        AtualizarStatus();
        if (_avisoResultado?.Tom == Tom.Erro)
        {
            Tarefa.ProgressState = TaskbarItemProgressState.Error;
            Tarefa.ProgressValue = 1;
        }
    }

    /// <summary>
    /// Leva o MP3 pronto da pasta interna para o destino. Se o Windows bloquear (Acesso controlado
    /// a pastas), o arquivo fica guardado e o aviso oferece salvar na pasta padrão, que não é protegida.
    /// </summary>
    private async Task<bool> EntregarPendenteAsync()
    {
        if (_pendente is null || _pastaPendente is null)
            return false;

        var pasta = Config.NomeAmigavel(_pastaPendente);
        try
        {
            var final = Baixador.Entregar(_pendente, _pastaPendente, _nomeArquivoPendente);
            _pendente = null;
            _baixada = true;
            var onde = Config.NomeAmigavel(Path.GetDirectoryName(final) ?? _pastaPendente);
            _avisoResultado = new Aviso(
                _descricaoPendente is null ? $"Salva em {onde}." : $"Salva em {onde}. {_descricaoPendente}.",
                Tom.Sucesso, "Abrir pasta", () => MostrarNoExplorer(final), final);
            return true;
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
        {
            var log = RegistrarErro($"Gravação em {_pastaPendente} recusada", erro.ToString());

            if (!File.Exists(_pendente))
            {
                _pendente = null;
                _avisoResultado = new Aviso($"Não consegui gravar em {pasta}.", Tom.Erro,
                    "Detalhes", () => AbrirArquivo(log), erro.Message);
            }
            else if (!MesmaPasta(_pastaPendente, Config.PastaPadrao) && await AcessoControlado.EstaLigadoAsync())
            {
                _avisoResultado = new Aviso($"O Windows protege {pasta} e bloqueou o Faixa.", Tom.Erro,
                    "Salvar em Downloads", () => _ = UsarPastaPadraoAsync(), erro.Message);
            }
            else
            {
                _avisoResultado = new Aviso($"Não consegui gravar em {pasta}.", Tom.Erro,
                    "Tentar de novo", () => _ = TentarEntregarDeNovoAsync(), erro.Message);
            }
            return false;
        }
    }

    private async Task TentarEntregarDeNovoAsync()
    {
        if (_pendente is null || _baixando)
            return;
        MostrarStatus("Salvando…");
        await EntregarPendenteAsync();
        AtualizarBotao();
        AtualizarStatus();
    }

    /// <summary>Volta para a pasta padrão (Downloads › Músicas) e entrega o que estiver pendente.</summary>
    private async Task UsarPastaPadraoAsync()
    {
        _config.PastaDestino = null;
        _config.Salvar();
        AtualizarPasta();

        if (_pendente is not null && !_baixando)
        {
            _pastaPendente = Config.PastaPadrao;
            await TentarEntregarDeNovoAsync();
            return;
        }
        _avisoResultado = null;
        AtualizarStatus();
    }

    /// <summary>
    /// Músicas que ficaram prontas mas não foram entregues (bloqueio, Faixa fechado no meio)
    /// vão para a pasta atual ao abrir; se ela recusar, para a padrão.
    /// </summary>
    private void EntregarSobras()
    {
        string[] sobras;
        try
        {
            sobras = Directory.Exists(Componentes.PastaProntos)
                ? Directory.GetFiles(Componentes.PastaProntos, "*.mp3")
                : [];
        }
        catch
        {
            return;
        }

        string? pastaFinal = null;
        foreach (var arquivo in sobras)
        {
            foreach (var pasta in new[] { _config.PastaEfetiva, Config.PastaPadrao }.Distinct())
            {
                try
                {
                    Baixador.Entregar(arquivo, pasta);
                    pastaFinal = pasta;
                    break;
                }
                catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
                {
                    // tenta a próxima pasta
                }
            }
        }

        if (pastaFinal is null)
            return;
        var nome = Config.NomeAmigavel(pastaFinal);
        var texto = sobras.Length == 1
            ? $"A música que tinha ficado pendente foi salva em {nome}."
            : $"{sobras.Length} músicas que tinham ficado pendentes foram salvas em {nome}.";
        _avisoResultado = new Aviso(texto, Tom.Sucesso, "Abrir pasta", () => AbrirPasta(pastaFinal));
        AtualizarStatus();
    }

    private static bool MesmaPasta(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void MostrarProgresso(Progresso p)
    {
        if (!_baixando)
            return;

        switch (p.Fase)
        {
            case Fase.Iniciando:
                MostrarStatus("Conectando ao YouTube…");
                BarraIndeterminada();
                break;
            case Fase.Baixando when p.Fracao is { } f:
                MostrarStatus("Baixando o áudio…", percentual: $"{(int)(f * 100)}%");
                BarraDeterminada(f);
                break;
            case Fase.Baixando:
                MostrarStatus("Baixando o áudio…");
                BarraIndeterminada();
                break;
            case Fase.Convertendo:
                MostrarStatus(_opcaoEmAndamento is { SemConversao: true }
                    ? "Gravando o áudio original, sem conversão…"
                    : $"Convertendo para MP3 {_opcaoEmAndamento?.Kbps ?? 320} kbps…");
                BarraIndeterminada();
                break;
            case Fase.Finalizando:
                MostrarStatus("Gravando a capa e as informações…");
                BarraIndeterminada();
                break;
        }
    }

    // ───────────────────────── Qualidade e formato ─────────────────────────

    private OpcaoFormato OpcaoAtual =>
        SeletorFormato.SelectedItem as OpcaoFormato ?? Formatos.Escolher(_opcoes, Formatos.Padrao);

    /// <summary>Refaz a lista do seletor (com ou sem os originais) mantendo a escolha salva.</summary>
    private void MontarOpcoes()
    {
        _montandoOpcoes = true;
        _opcoes = _modoLista
            ? Formatos.Montar(null, _faixas.Where(f => f.Selecionada && f.Item.Disponivel).Sum(f => f.Item.Duracao ?? 0))
            : Formatos.Montar(_audio);
        SeletorFormato.ItemsSource = _opcoes;
        SeletorFormato.SelectedItem = Formatos.Escolher(_opcoes, _config.Formato);
        _montandoOpcoes = false;
    }

    private void Formato_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_montandoOpcoes || SeletorFormato.SelectedItem is not OpcaoFormato opcao)
            return;
        _config.Formato = opcao.Chave;
        if (!Capturando)
            _config.Salvar();
        MostrarOrigem();

        // Já baixou neste formato? Escolher outro libera baixar de novo.
        if (_baixada)
        {
            _baixada = false;
            _avisoResultado = null;
            AtualizarBotao();
        }
        AtualizarStatus();
    }

    private void ZerarAnalise()
    {
        _ctsAnalise?.Cancel();
        _ctsAnalise = null;
        _audio = null;
        _analisando = false;
        _falhaAnalise = null;
        _avisoAnalise = null;
        MontarOpcoes();
        MostrarFonte();
    }

    private void AnalisarSePreciso()
    {
        if (_prontos && _modoLista && _lista is null && !_carregandoLista && !_falhaLista && _link?.ListaId is { } listaId && !Capturando)
            _ = CarregarListaAsync(listaId);
        else if (_prontos && _video is { } video && !_analisando && _audio is null && _falhaAnalise is null && !Capturando)
            _ = AnalisarAsync(video.Id);
        else
            MostrarFonte();
    }

    /// <summary>Pergunta ao yt-dlp quais formatos de áudio o vídeo tem (sem baixar nada).</summary>
    private async Task AnalisarAsync(string id)
    {
        var cts = _ctsAnalise = CancellationTokenSource.CreateLinkedTokenSource(_ctsJanela.Token);
        _analisando = true;
        MostrarFonte();
        AtualizarBotao();
        AtualizarStatus();

        try
        {
            if (_atualizacao is { } atualizacao)
            {
                await atualizacao; // o yt-dlp.exe não pode ser trocado enquanto roda
                _atualizacao = null;
            }
            var audio = await Analisador.AnalisarAsync(id, cts.Token);
            if (cts.IsCancellationRequested || _video?.Id != id)
                return;
            _audio = audio;
            if (!_nomeEditado && audio.Dados is { } dados)
                DefinirNome(Metadados.Resolver(dados));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception erro)
        {
            if (cts.IsCancellationRequested || _video?.Id != id)
                return;
            // A análise falhar não impede o download: ele cai no "melhor áudio" e conta o que veio.
            if (erro is ErroDownload falha)
            {
                RegistrarErro("Análise do áudio: " + falha.Message, falha.Detalhe);
                _falhaAnalise = falha.Message;
                _avisoAnalise = new Aviso(falha.Message, Tom.Erro, Detalhe: falha.Detalhe);
            }
            else
            {
                RegistrarErro("Análise do áudio", erro.ToString());
                _falhaAnalise = erro.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_ctsAnalise, cts))
                _analisando = false;
        }

        MontarOpcoes();
        MostrarFonte();
        AtualizarBotao();
        AtualizarStatus();
    }

    /// <summary>A linha "No YouTube: Opus · 129 kbps · 48 kHz · 3:33" embaixo do título.</summary>
    private void MostrarFonte()
    {
        string? texto = null;
        var destaque = false;

        if (_video is null)
            texto = null;
        else if (_audio is { } audio)
        {
            var f = audio.Melhor;
            texto = "No YouTube: " + string.Join(" · ", new[]
            {
                f.Nome, f.KbpsTexto, f.KhzTexto, audio.Duracao is double d ? Formatos.Duracao(d) : null,
            }.Where(x => x is not null));
            destaque = true;
        }
        else if (_analisando)
            texto = "Lendo a qualidade do áudio…";
        else if (_falhaAnalise is not null)
            texto = "Qualidade original indisponível";
        else if (!_prontos)
            texto = "A qualidade aparece depois do preparo";

        Fonte.Visibility = texto is null ? Visibility.Hidden : Visibility.Visible;
        FonteTexto.Text = texto ?? "";
        FonteTexto.Foreground = Pincel(destaque ? "Texto" : "TextoSuave");
        FonteIcone.Foreground = Pincel(destaque ? "Destaque" : "TextoApagado");
    }

    /// <summary>"Opus 129 kbps → MP3 320 kbps", com a fonte que o yt-dlp disse ter baixado.</summary>
    private static string DescreverResultado(OpcaoFormato opcao, Resultado r)
    {
        var fonte = r.FonteCodec is string codec && r.FonteKbps is double kbps
            ? $"{Analisador.NomeDoCodec(codec)} {Math.Round(kbps):0} kbps"
            : null;
        return opcao.SemConversao
            ? (fonte is null ? "Áudio original, sem conversão" : $"{fonte}, sem conversão")
            : (fonte is null ? $"MP3 {opcao.Kbps} kbps" : $"{fonte} → MP3 {opcao.Kbps} kbps");
    }

    // ───────────────────────── Playlist ─────────────────────────

    private void MostrarModoLista(bool lista)
    {
        _modoLista = lista;
        PainelVideo.Visibility = lista ? Visibility.Hidden : Visibility.Visible;
        PainelLista.Visibility = lista ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"Playlist inteira": o link tinha vídeo e playlist, e a pessoa quer a playlist.</summary>
    private void AbrirPlaylistDoLink()
    {
        if (_link?.ListaId is { } listaId && !_baixando)
            _ = CarregarListaAsync(listaId);
    }

    /// <summary>"Só esta música": volta ao vídeo do link.</summary>
    private void VoltarAoVideo()
    {
        if (_link?.VideoId is not { } id || _baixando)
            return;
        ZerarTudo();
        _ = CarregarPreviaAsync(id);
        AtualizarBotao();
        AtualizarStatus();
    }

    private async Task CarregarListaAsync(string listaId)
    {
        ZerarTudo();
        MostrarModoLista(true);
        ListaTipo.Text = "PLAYLIST";
        ListaTitulo.Text = _prontos ? "Lendo a playlist…" : "A playlist abre depois do preparo";
        ListaResumo.Text = "";
        ListaSelecao.Text = "";
        ListaCapa.Background = Pincel("Superficie2");
        ListaCapaIcone.Visibility = Visibility.Visible;
        MarcarTodas.IsChecked = false;
        MontarOpcoes();
        if (!_prontos)
        {
            AtualizarBotao();
            AtualizarStatus();
            return; // AnalisarSePreciso chama de novo quando o preparo terminar
        }

        var cts = _ctsLista = CancellationTokenSource.CreateLinkedTokenSource(_ctsJanela.Token);
        _carregandoLista = true;
        AtualizarBotao();
        AtualizarStatus();

        try
        {
            if (_atualizacao is { } atualizacao)
            {
                await atualizacao;
                _atualizacao = null;
            }
            var lista = await Playlists.ListarAsync(listaId, cts.Token);
            if (cts.IsCancellationRequested)
                return;
            MostrarLista(lista);
            _ = CarregarCapaDaListaAsync(lista, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception erro)
        {
            if (cts.IsCancellationRequested)
                return;
            _falhaLista = true;
            ListaTitulo.Text = "Não consegui abrir a playlist";
            if (erro is ErroDownload falha)
            {
                RegistrarErro("Playlist: " + falha.Message, falha.Detalhe);
                _avisoAnalise = new Aviso(falha.Message, Tom.Erro, Detalhe: falha.Detalhe);
            }
            else
            {
                var log = RegistrarErro("Playlist", erro.ToString());
                _avisoAnalise = new Aviso("Não consegui ler a playlist.", Tom.Erro, "Detalhes", () => AbrirArquivo(log), erro.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_ctsLista, cts))
                _carregandoLista = false;
        }

        AtualizarSelecao();
        AtualizarStatus();
    }

    private void MostrarLista(InfoLista lista)
    {
        _lista = lista;
        _faixas.Clear();
        foreach (var item in lista.Itens)
            _faixas.Add(new FaixaDaLista(item));

        ListaTipo.Text = lista.EhAlbum ? "ÁLBUM" : "PLAYLIST";
        ListaTitulo.Text = lista.Titulo;
        var partes = new List<string>();
        if (lista.Canal.Length > 0)
            partes.Add(lista.Canal);
        partes.Add(lista.Disponiveis == 1 ? "1 música" : $"{lista.Disponiveis} músicas");
        if (lista.DuracaoTotal > 0)
            partes.Add(Formatos.DuracaoLonga(lista.DuracaoTotal));
        var fora = lista.Itens.Count - lista.Disponiveis;
        if (fora > 0)
            partes.Add(fora == 1 ? "1 indisponível" : $"{fora} indisponíveis");
        ListaResumo.Text = string.Join(" · ", partes);
        ListaResumo.ToolTip = ListaResumo.Text;
    }

    private async Task CarregarCapaDaListaAsync(InfoLista lista, CancellationToken ct)
    {
        var primeira = lista.Itens.FirstOrDefault(i => i.Disponivel);
        if (primeira is null)
            return;
        try
        {
            var bytes = await YouTube.BuscarCapaAsync(primeira.Id, ct);
            if (ct.IsCancellationRequested || bytes is null || CarregarImagem(bytes) is not { } imagem)
                return;
            ListaCapa.Background = new ImageBrush(imagem) { Stretch = Stretch.UniformToFill };
            ListaCapaIcone.Visibility = Visibility.Collapsed;
        }
        catch (Exception erro) when (erro is OperationCanceledException or System.Net.Http.HttpRequestException)
        {
            // Sem capa: fica o ícone.
        }
    }

    /// <summary>Contagem de marcadas, caixa "Todas", tamanhos do seletor e texto do botão.</summary>
    private void AtualizarSelecao()
    {
        var disponiveis = _faixas.Count(f => f.Item.Disponivel);
        var marcadas = _faixas.Count(f => f.Selecionada && f.Item.Disponivel);
        ListaSelecao.Text = _lista is null ? "" : $"{marcadas} de {disponiveis} marcadas";
        MarcarTodas.IsChecked = marcadas == 0 ? false : marcadas == disponiveis ? true : null;
        MontarOpcoes();
        AtualizarBotao();
    }

    private void MarcarTodas_Click(object sender, RoutedEventArgs e)
    {
        var marcar = _faixas.Any(f => f.PodeMarcar && !f.Selecionada);
        foreach (var faixa in _faixas.Where(f => f.PodeMarcar))
            faixa.Selecionada = marcar;
        AtualizarSelecao();
        AtualizarStatus();
    }

    private void MarcarFaixa_Click(object sender, RoutedEventArgs e)
    {
        AtualizarSelecao();
        AtualizarStatus();
    }

    /// <summary>Baixa as faixas marcadas, uma de cada vez, numa subpasta com o nome da playlist.</summary>
    private async Task BaixarListaAsync()
    {
        if (_lista is not { } lista || !_prontos || _baixando)
            return;
        var fila = _faixas.Where(f => f.Pendente).ToList();
        if (fila.Count == 0)
            return;

        _baixando = true;
        _avisoResultado = null;
        var cts = _ctsDownload = new CancellationTokenSource();
        var opcao = _opcaoEmAndamento = OpcaoAtual;
        var pasta = Path.Combine(_config.PastaEfetiva, Metadados.Sanitizar(lista.Titulo));
        foreach (var faixa in fila)
        {
            faixa.Estado = EstadoFaixa.Aguardando;
            faixa.Percentual = 0;
        }
        AtualizarBotao();

        int feitas = 0, falhas = 0;
        var cancelado = false;
        var atualizou = false;
        try
        {
            if (_atualizacao is { } atualizacao)
            {
                MostrarStatus("Atualizando o yt-dlp…");
                BarraIndeterminada();
                await atualizacao;
                _atualizacao = null;
            }

            for (var i = 0; i < fila.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                var faixa = fila[i];
                var indice = i;
                faixa.Estado = EstadoFaixa.Baixando;
                ListaFaixas.ScrollIntoView(faixa);
                var progresso = new Progress<Progresso>(p => MostrarProgressoLista(faixa, indice, fila.Count, p));

                try
                {
                    Resultado r;
                    try
                    {
                        r = await Baixador.BaixarAsync(faixa.Item.Id, opcao, null, progresso, cts.Token);
                    }
                    catch (ErroDownload erro) when (erro.ValeAtualizar && !atualizou
                                                    && DateTime.Now - _config.UltimaAtualizacaoYtDlp > TimeSpan.FromHours(1))
                    {
                        atualizou = true;
                        RegistrarErro("Primeira tentativa", erro.Detalhe);
                        MostrarStatus("Atualizando o yt-dlp e tentando de novo…");
                        BarraIndeterminada();
                        await AtualizarYtDlpAsync();
                        r = await Baixador.BaixarAsync(faixa.Item.Id, opcao, null, progresso, cts.Token);
                    }

                    var nome = r.Dados is { } dados ? Metadados.Resolver(dados) : faixa.Nome;
                    if (lista.EhAlbum && nome.Album is null)
                        nome = nome with { Album = lista.Titulo };
                    int? numero = lista.EhAlbum ? faixa.Numero : null;
                    GravarEtiquetas(r.Arquivo, nome, numero);
                    Baixador.Entregar(r.Arquivo, pasta,
                        Metadados.NomeDeArquivo(nome, Path.GetExtension(r.Arquivo).TrimStart('.'), numero));

                    faixa.Nome = nome;
                    faixa.Estado = EstadoFaixa.Pronta;
                    faixa.Detalhe = DescreverResultado(opcao, r);
                    feitas++;
                }
                catch (ErroDownload erro)
                {
                    RegistrarErro($"{faixa.Musica}: {erro.Message}", erro.Detalhe);
                    faixa.Estado = EstadoFaixa.Falhou;
                    faixa.Detalhe = erro.Message;
                    falhas++;
                }
                catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
                {
                    RegistrarErro($"{faixa.Musica}: gravação recusada", erro.ToString());
                    faixa.Estado = EstadoFaixa.Falhou;
                    faixa.Detalhe = $"Não consegui gravar em {Config.NomeAmigavel(pasta)}.";
                    falhas++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelado = true;
        }
        catch (Exception erro)
        {
            var log = RegistrarErro("Falha ao rodar o yt-dlp", erro.ToString());
            _avisoResultado = new Aviso("Não consegui rodar o yt-dlp. O antivírus pode ter bloqueado.", Tom.Erro,
                "Detalhes", () => AbrirArquivo(log), erro.Message);
        }
        finally
        {
            foreach (var faixa in fila.Where(f => f.Estado == EstadoFaixa.Baixando))
                faixa.Estado = EstadoFaixa.Aguardando;
            _baixando = false;
            _ctsDownload = null;
            cts.Dispose();
        }

        var onde = Config.NomeAmigavel(pasta);
        Action? abrir = feitas > 0 ? () => AbrirPasta(pasta) : null;
        _avisoResultado ??= cancelado
            ? new Aviso($"Download cancelado. {feitas} de {fila.Count} salvas em {onde}.", Tom.Neutro, abrir is null ? null : "Abrir pasta", abrir)
            : falhas == 0
                ? new Aviso(feitas == 1 ? $"1 música salva em {onde}." : $"{feitas} músicas salvas em {onde}.", Tom.Sucesso, "Abrir pasta", abrir, pasta)
                : new Aviso($"{feitas} de {fila.Count} salvas. {(falhas == 1 ? "1 falhou" : $"{falhas} falharam")}: passe o mouse na faixa para ver o motivo.",
                    Tom.Erro, abrir is null ? null : "Abrir pasta", abrir);

        AtualizarSelecao();
        AtualizarStatus();
        if (_avisoResultado.Tom == Tom.Erro)
        {
            Tarefa.ProgressState = TaskbarItemProgressState.Error;
            Tarefa.ProgressValue = 1;
        }
    }

    private void MostrarProgressoLista(FaixaDaLista faixa, int indice, int total, Progresso p)
    {
        if (!_baixando)
            return;

        var fracao = p.Fase switch
        {
            Fase.Baixando when p.Fracao is { } f => f * 0.85,
            Fase.Convertendo => 0.9,
            Fase.Finalizando => 0.97,
            _ => 0.02,
        };
        faixa.Percentual = fracao;
        var geral = (indice + fracao) / total;
        var etapa = p.Fase switch
        {
            Fase.Convertendo => opcaoSemConversao() ? "gravando" : "convertendo",
            Fase.Finalizando => "gravando a capa",
            _ => "baixando",
        };
        MostrarStatus($"{indice + 1} de {total}, {etapa}: {faixa.Musica}", percentual: $"{(int)(geral * 100)}%");
        BarraDeterminada(geral);

        bool opcaoSemConversao() => _opcaoEmAndamento?.SemConversao == true;
    }

    // ───────────────────────── Atualização do Faixa ─────────────────────────

    private async Task ProcurarVersaoNovaAsync()
    {
        try
        {
            _versaoNova = await Atualizador.ProcurarAsync(_ctsJanela.Token);
        }
        catch
        {
            return; // sem internet, GitHub fora do ar, repositório inexistente: tenta na próxima abertura
        }
        if (_versaoNova is null)
            return;
        BotaoAtualizarTexto.Text = $"Versão {_versaoNova.Texto}";
        BotaoAtualizar.ToolTip = $"Há uma versão nova do Faixa ({_versaoNova.Texto}). Você está na {Atualizador.VersaoAtualTexto}.";
        BotaoAtualizar.Visibility = Visibility.Visible;
    }

    private async void Atualizar_Click(object sender, RoutedEventArgs e)
    {
        if (_versaoNova is not { } nova || _baixando || _atualizando)
            return;

        var notas = string.IsNullOrWhiteSpace(nova.Notas) ? "" : $"\n\nNovidades:\n{nova.Notas}";
        var resposta = MessageBox.Show(this,
            $"Atualizar o Faixa da versão {Atualizador.VersaoAtualTexto} para a {nova.Texto}?" +
            $"\n\nO programa baixa a versão nova, confere o arquivo e reabre sozinho.{notas}",
            "Atualizar o Faixa", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (resposta != MessageBoxResult.Yes)
            return;

        _atualizando = true;
        AtualizarBotao();
        string? baixado = null;
        try
        {
            MostrarStatus($"Baixando a versão {nova.Texto}…");
            baixado = await Atualizador.BaixarAsync(nova, new Progress<double>(f =>
            {
                MostrarStatus($"Baixando a versão {nova.Texto}…", percentual: $"{(int)(f * 100)}%");
                BarraDeterminada(f);
            }), _ctsJanela.Token);

            var exe = Atualizador.Instalar(baixado);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception erro) when (erro is UnauthorizedAccessException or IOException && baixado is not null && File.Exists(baixado))
        {
            // A pasta do Faixa não aceita a troca (ex.: protegida pelo Windows): entrega o .exe novo em Downloads.
            RegistrarErro("Atualização: troca do .exe recusada", erro.ToString());
            var destino = Path.Combine(Config.PastaDownloads, $"Faixa-{nova.Texto}.exe");
            File.Move(baixado, destino, overwrite: true);
            _avisoResultado = new Aviso($"Não consegui trocar o programa na pasta dele. A versão {nova.Texto} ficou em Downloads: feche o Faixa e use o arquivo novo.",
                Tom.Erro, "Abrir pasta", () => MostrarNoExplorer(destino));
        }
        catch (Exception erro)
        {
            var log = RegistrarErro("Atualização do Faixa", erro.ToString());
            _avisoResultado = new Aviso(erro is InvalidDataException ? erro.Message : "Não consegui baixar a versão nova. Tente mais tarde.",
                Tom.Erro, "Detalhes", () => AbrirArquivo(log), erro.Message);
        }
        finally
        {
            _atualizando = false;
        }
        AtualizarBotao();
        AtualizarStatus();
    }

    // ───────────────────────── Pasta de destino ─────────────────────────

    private void AtualizarPasta()
    {
        var pasta = _config.PastaEfetiva;
        PastaNome.Text = Config.NomeAmigavel(pasta);
        PainelPasta.ToolTip = pasta + Environment.NewLine + "Clique para abrir";
    }

    private async void Alterar_Click(object sender, RoutedEventArgs e)
    {
        var atual = _config.PastaEfetiva;
        var dialogo = new OpenFolderDialog
        {
            Title = "Onde salvar as músicas?",
            InitialDirectory = Directory.Exists(atual) ? atual : Config.PastaDownloads,
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        _config.PastaDestino = dialogo.FolderName;
        _config.Salvar();
        AtualizarPasta();

        if (AcessoControlado.PastaProtegida(dialogo.FolderName) && await AcessoControlado.EstaLigadoAsync())
        {
            _avisoResultado = new Aviso(
                $"O Windows protege {Config.NomeAmigavel(dialogo.FolderName)} e vai bloquear o Faixa.",
                Tom.Erro, "Usar Downloads", () => _ = UsarPastaPadraoAsync());
            AtualizarStatus();
        }
    }

    private void Pasta_Click(object sender, MouseButtonEventArgs e) => AbrirPasta(_config.PastaEfetiva);

    private static void AbrirPasta(string pasta)
    {
        try
        {
            Directory.CreateDirectory(pasta);
        }
        catch
        {
            // Se não der para criar, o Explorer mostra o erro.
        }
        AbrirArquivo(pasta);
    }

    private void Acao_Click(object sender, RoutedEventArgs e) => _acaoStatus?.Invoke();

    private static void MostrarNoExplorer(string arquivo)
    {
        if (File.Exists(arquivo))
            Process.Start("explorer.exe", $"/select,\"{arquivo}\"");
        else
            AbrirArquivo(Path.GetDirectoryName(arquivo) ?? Config.PastaMusicas);
    }

    private static void AbrirArquivo(string caminho)
    {
        try
        {
            Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });
        }
        catch
        {
            // Nada a fazer se o Windows não souber abrir.
        }
    }

    private static string RegistrarErro(string titulo, string detalhe)
    {
        var arquivo = Path.Combine(Componentes.PastaApp, "ultimo-erro.txt");
        try
        {
            Directory.CreateDirectory(Componentes.PastaApp);
            File.WriteAllText(arquivo, $"{DateTime.Now:dd/MM/yyyy HH:mm:ss}  {titulo}{Environment.NewLine}{Environment.NewLine}{detalhe}{Environment.NewLine}");
        }
        catch
        {
            // Log é só ajuda; não pode derrubar nada.
        }
        return arquivo;
    }

    // ───────────────────────── Estado visual ─────────────────────────

    private void AtualizarBotao()
    {
        var pendentes = _modoLista ? _faixas.Count(f => f.Pendente) : 0;
        var listaPronta = _modoLista && pendentes == 0 && _faixas.Any(f => f.Selecionada && f.Estado == EstadoFaixa.Pronta);
        var (icone, texto, habilitado) =
            _baixando ? (IconeCancelar, "Cancelar", true)
            : _modoLista && _carregandoLista ? (IconeBaixar, "Lendo a playlist…", false)
            : _modoLista && !_prontos ? (IconeBaixar, _preparando ? "Aguarde o preparo" : "Baixar", false)
            : _modoLista && listaPronta ? (IconeOk, "Baixadas", false)
            : _modoLista ? (IconeBaixar, pendentes switch { 0 => "Marque as músicas", 1 => "Baixar 1 música", _ => $"Baixar {pendentes} músicas" }, pendentes > 0 && _lista is not null)
            : _baixada ? (IconeOk, "Baixada", false)
            : _video is null ? (IconeBaixar, "Baixar", false)
            : !_prontos ? (IconeBaixar, _preparando ? "Aguarde o preparo" : "Baixar", false)
            // Espera a análise para que a escolha de formato seja a definitiva.
            : _analisando ? (IconeBaixar, "Lendo o áudio…", false)
            : (IconeBaixar, "Baixar", true);

        BotaoBaixar.Style = (Style)FindResource(_baixando ? "BotaoSecundario" : "BotaoPrincipal");
        BotaoBaixar.Tag = !_baixando && (_modoLista ? listaPronta : _baixada) ? "ok" : null;
        BotaoBaixar.IsEnabled = habilitado;
        BotaoIcone.Text = icone;
        BotaoTexto.Text = texto;

        CampoLink.IsReadOnly = _baixando;
        BotaoColar.IsEnabled = !_baixando;
        BotaoLimpar.IsEnabled = !_baixando;
        BotaoAlterar.IsEnabled = !_baixando;
        BotaoAtualizar.IsEnabled = !_baixando && !_atualizando;
        if (_atualizando)
            BotaoBaixar.IsEnabled = false;
        SeletorFormato.IsEnabled = !_baixando;
        CampoMusica.IsReadOnly = CampoArtista.IsReadOnly = _baixando || _nome is null;
        MarcarTodas.IsEnabled = !_baixando && _lista is not null;
    }

    /// <summary>Decide o que mostrar no status quando não há download rodando.</summary>
    private void AtualizarStatus()
    {
        if (_baixando)
            return;

        if (_avisoLink is null && _fracaoPreparo is { } f)
        {
            MostrarStatus($"Primeira vez: baixando os componentes ({_mbPreparo} MB)", percentual: $"{(int)(f * 100)}%");
            BarraDeterminada(f);
            return;
        }

        BarraOculta();
        var aviso = _avisoLink ?? _avisoPreparo ?? _avisoResultado ?? _avisoAnalise;
        if (aviso is not null)
            MostrarStatus(aviso.Texto, aviso.Tom, acao: aviso.Acao, aoClicar: aviso.AoClicar, detalhe: aviso.Detalhe);
        else if (_modoLista && _lista is not null)
            MostrarStatus(Formatos.ExplicarLista(OpcaoAtual),
                acao: _link?.VideoId is not null ? "Só esta música" : null, aoClicar: VoltarAoVideo);
        else if (_video is not null && _audio is not null)
            MostrarStatus(Formatos.Explicar(OpcaoAtual, _audio),
                acao: _link?.ListaId is not null ? "Playlist inteira" : null, aoClicar: AbrirPlaylistDoLink);
        else if (_video is not null && _link?.ListaId is not null)
            MostrarStatus("Esse link também tem uma playlist.", acao: "Playlist inteira", aoClicar: AbrirPlaylistDoLink);
        else
            MostrarStatus("");
    }

    private void MostrarStatus(string texto, Tom tom = Tom.Neutro, string? percentual = null,
        string? acao = null, Action? aoClicar = null, string? detalhe = null)
    {
        StatusTexto.Text = texto;
        StatusTexto.ToolTip = string.IsNullOrWhiteSpace(detalhe) ? null : detalhe;
        StatusTexto.Foreground = Pincel(tom switch { Tom.Erro => "Erro", Tom.Sucesso => "Texto", _ => "TextoSuave" });

        StatusIcone.Visibility = tom == Tom.Neutro ? Visibility.Collapsed : Visibility.Visible;
        StatusIcone.Text = tom == Tom.Sucesso ? IconeOk : IconeAlerta;
        StatusIcone.Foreground = Pincel(tom == Tom.Sucesso ? "Sucesso" : "Erro");

        StatusPercentual.Text = percentual ?? "";
        StatusPercentual.Visibility = percentual is null ? Visibility.Collapsed : Visibility.Visible;

        _acaoStatus = aoClicar;
        BotaoAcao.Content = acao;
        BotaoAcao.Visibility = acao is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MostrarSemNome(string texto)
    {
        _definindoNome = true;
        CampoMusica.Text = texto;
        CampoArtista.Text = "";
        _definindoNome = false;
        CampoMusica.IsReadOnly = CampoArtista.IsReadOnly = true;
        CampoMusica.Foreground = Pincel("TextoApagado");
        LinhaOrigem.Text = "";
        LinhaOrigem.ToolTip = null;
    }

    private void DefinirNome(NomeMusica nome)
    {
        _nome = nome;
        _definindoNome = true;
        CampoMusica.Text = nome.Musica;
        CampoArtista.Text = nome.Artista;
        _definindoNome = false;
        CampoMusica.IsReadOnly = CampoArtista.IsReadOnly = _baixando;
        CampoMusica.Foreground = Pincel("Texto");
        MostrarOrigem();
    }

    /// <summary>A linha que diz de onde veio o nome, e a dica com o nome do arquivo.</summary>
    private void MostrarOrigem()
    {
        if (_nome is null)
        {
            LinhaOrigem.Text = "";
            return;
        }

        var (texto, cor) = _nome.Origem switch
        {
            OrigemNome.Oficial => ("Dados oficiais do YouTube", "Sucesso"),
            OrigemNome.Titulo => ("Separado do título do vídeo · clique para corrigir", "TextoApagado"),
            OrigemNome.Canal => ("Artista = nome do canal · clique para corrigir", "TextoApagado"),
            _ => ("Editado por você", "TextoSuave"),
        };
        LinhaOrigem.Text = texto;
        LinhaOrigem.Foreground = Pincel(cor);
        LinhaOrigem.ToolTip = "O arquivo vai se chamar: " + Metadados.NomeDeArquivo(_nome, OpcaoAtual.Extensao);
    }

    private void CampoNome_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_definindoNome || _nome is null)
            return;
        _nomeEditado = true;
        _nome = _nome with
        {
            Musica = CampoMusica.Text.Trim(),
            Artista = CampoArtista.Text.Trim(),
            Origem = OrigemNome.Editado,
        };
        MostrarOrigem();
    }

    private void GravarEtiquetas(string arquivo, NomeMusica nome, int? faixa)
    {
        try
        {
            Etiquetas.Gravar(arquivo, nome, faixa);
        }
        catch (Exception erro)
        {
            // O arquivo continua bom, só fica com os dados que o yt-dlp gravou.
            RegistrarErro("Gravação das informações da música", erro.ToString());
        }
    }

    private void MostrarCapaVazia(string texto, string icone = IconeNota, bool pulsando = false)
    {
        Capa.BeginAnimation(OpacityProperty, null);
        Capa.Opacity = 0;
        Capa.Background = null;
        CapaVazia.Visibility = Visibility.Visible;
        CapaVaziaTexto.Text = texto;
        CapaVaziaIcone.Text = icone;

        if (pulsando && !Capturando)
        {
            CapaVazia.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(750))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
        }
        else
        {
            CapaVazia.BeginAnimation(OpacityProperty, null);
            CapaVazia.Opacity = 1;
        }
    }

    private void MostrarCapa(byte[]? bytes)
    {
        var imagem = bytes is null ? null : CarregarImagem(bytes);
        if (imagem is null)
        {
            MostrarCapaVazia("Capa indisponível");
            return;
        }

        CapaVazia.BeginAnimation(OpacityProperty, null);
        CapaVazia.Visibility = Visibility.Hidden;
        // Mesmo corte quadrado e central que vai para dentro do MP3.
        Capa.Background = new ImageBrush(imagem) { Stretch = Stretch.UniformToFill };

        if (Capturando)
        {
            Capa.Opacity = 1;
            return;
        }

        var duracao = TimeSpan.FromMilliseconds(280);
        var suave = new CubicEase { EasingMode = EasingMode.EaseOut };
        Capa.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duracao) { EasingFunction = suave });
        CapaEscala.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, duracao) { EasingFunction = suave });
        CapaEscala.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, duracao) { EasingFunction = suave });
    }

    private static BitmapImage? CarregarImagem(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // Barra de progresso própria: a do WPF não tem cantos arredondados nem modo indeterminado bonito.

    private void BarraDeterminada(double fracao)
    {
        Trilho.Visibility = Visibility.Visible;
        if (_modoBarra != ModoBarra.Determinada)
        {
            BarraDesloc.BeginAnimation(TranslateTransform.XProperty, null);
            BarraDesloc.X = 0;
            Barra.BeginAnimation(WidthProperty, null);
            Barra.Width = 0;
        }
        _modoBarra = ModoBarra.Determinada;
        _fracaoBarra = fracao;

        var alvo = Trilho.ActualWidth * fracao;
        if (Capturando)
        {
            Barra.Width = alvo;
        }
        else
        {
            Barra.BeginAnimation(WidthProperty, new DoubleAnimation(alvo, TimeSpan.FromMilliseconds(200)));
        }

        Tarefa.ProgressState = TaskbarItemProgressState.Normal;
        Tarefa.ProgressValue = fracao;
    }

    private void BarraIndeterminada()
    {
        Trilho.Visibility = Visibility.Visible;
        Tarefa.ProgressState = TaskbarItemProgressState.Indeterminate;
        if (_modoBarra == ModoBarra.Indeterminada)
            return;
        _modoBarra = ModoBarra.Indeterminada;
        ReajustarBarra();
    }

    private void BarraOculta()
    {
        _modoBarra = ModoBarra.Oculta;
        Trilho.Visibility = Visibility.Hidden;
        BarraDesloc.BeginAnimation(TranslateTransform.XProperty, null);
        BarraDesloc.X = 0;
        Barra.BeginAnimation(WidthProperty, null);
        Barra.Width = 0;
        Tarefa.ProgressState = TaskbarItemProgressState.None;
    }

    private void ReajustarBarra()
    {
        var largura = Trilho.ActualWidth;
        if (largura <= 0)
            return;

        switch (_modoBarra)
        {
            case ModoBarra.Determinada:
                Barra.BeginAnimation(WidthProperty, null);
                Barra.Width = largura * _fracaoBarra;
                break;

            case ModoBarra.Indeterminada:
                var trecho = largura * 0.3;
                Barra.BeginAnimation(WidthProperty, null);
                Barra.Width = trecho;
                if (Capturando)
                {
                    BarraDesloc.X = largura * 0.35;
                }
                else
                {
                    BarraDesloc.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-trecho, largura, TimeSpan.FromSeconds(1.4))
                    {
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    });
                }
                break;
        }
    }

    private Brush Pincel(string chave) => (Brush)FindResource(chave);

    // ───────────────────────── Capturas de tela (desenvolvimento) ─────────────────────────

    private const string JsonPlaylistDeExemplo = """
        {"_type": "playlist", "id": "PLcapturasfaixa", "title": "Clássicos dos 80", "uploader": "Rodrigo", "entries": [
          {"id": "dQw4w9WgXcQ", "title": "Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster)", "duration": 213, "channel": "Rick Astley"},
          {"id": "fJ9rUzIMcZQ", "title": "Queen – Bohemian Rhapsody (Official Video Remastered)", "duration": 359, "channel": "Queen Official"},
          {"id": "djV11Xbc914", "title": "a-ha - Take On Me (Official Video) [4K]", "duration": 245, "channel": "a-ha"},
          {"id": "1w7OgIMMRc4", "title": "Guns N' Roses - Sweet Child O' Mine (Official Music Video)", "duration": 356, "channel": "GunsNRosesVEVO"},
          {"id": "xxxxxxxxxx1", "title": "[Private video]", "duration": null},
          {"id": "Zi_XLOBDo_Y", "title": "Michael Jackson - Billie Jean (Official Video)", "duration": 294, "channel": "Michael Jackson"},
          {"id": "lDK9QqIzhwk", "title": "Bon Jovi - Livin' On A Prayer (Official Music Video)", "duration": 251, "channel": "Bon Jovi"}
        ]}
        """;

    private const string JsonDeExemplo = """
        {"id": "dQw4w9WgXcQ", "duration": 213, "formats": [
          {"format_id": "sb0", "vcodec": "none", "acodec": "none", "ext": "mhtml", "format_note": "storyboard"},
          {"format_id": "249", "vcodec": "none", "acodec": "opus", "abr": 49.8, "asr": 48000, "ext": "webm", "format_note": "low", "quality": 2, "language_preference": -1, "has_drm": false},
          {"format_id": "139", "vcodec": "none", "acodec": "mp4a.40.5", "abr": 48.8, "asr": 22050, "ext": "m4a", "format_note": "low", "quality": 2, "language_preference": -1, "has_drm": false},
          {"format_id": "140", "vcodec": "none", "acodec": "mp4a.40.2", "abr": 129.6, "tbr": 129.6, "asr": 44100, "ext": "m4a", "format_note": "medium", "quality": 3, "language_preference": -1, "filesize": 3449447, "has_drm": false},
          {"format_id": "251-drc", "vcodec": "none", "acodec": "opus", "abr": 131.2, "asr": 48000, "ext": "webm", "format_note": "medium, DRC", "quality": 2.5, "language_preference": -1, "has_drm": false},
          {"format_id": "251", "vcodec": "none", "acodec": "opus", "abr": 129.5, "tbr": 129.5, "asr": 48000, "ext": "webm", "format_note": "medium", "quality": 3, "language_preference": -1, "filesize": 3437753, "has_drm": false},
          {"format_id": "18", "vcodec": "avc1.42001E", "acodec": "mp4a.40.2", "tbr": 503.9, "ext": "mp4", "quality": 1},
          {"format_id": "137", "vcodec": "avc1.640028", "acodec": "none", "tbr": 4400.1, "ext": "mp4", "quality": 8}
        ]}
        """;

    /// <summary>
    /// Faixa.exe --capturas &lt;pasta&gt; desenha cada estado da tela em PNG e fecha.
    /// Não baixa componentes nem músicas; só busca a prévia de um vídeo para ter capa real.
    /// </summary>
    private async Task GerarCapturasAsync()
    {
        var pasta = _pastaCapturas!;
        Directory.CreateDirectory(pasta);

        async Task Salvar(string nome)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            const double escala = 2;
            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(Raiz.ActualWidth * escala), (int)Math.Ceiling(Raiz.ActualHeight * escala),
                96 * escala, 96 * escala, PixelFormats.Pbgra32);
            rtb.Render(Raiz);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(rtb));
            await using var arquivo = File.Create(Path.Combine(pasta, nome + ".png"));
            png.Save(arquivo);
        }

        async Task SalvarElemento(FrameworkElement elemento, string nome)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            const double escala = 2, borda = 14;
            double w = elemento.ActualWidth, h = elemento.ActualHeight;
            var desenho = new DrawingVisual();
            using (var dc = desenho.RenderOpen())
            {
                dc.DrawRectangle(Pincel("Fundo"), null, new Rect(0, 0, w + 2 * borda, h + 2 * borda));
                dc.DrawRectangle(new VisualBrush(elemento), null, new Rect(borda, borda, w, h));
            }
            var rtb = new RenderTargetBitmap((int)Math.Ceiling((w + 2 * borda) * escala), (int)Math.Ceiling((h + 2 * borda) * escala),
                96 * escala, 96 * escala, PixelFormats.Pbgra32);
            rtb.Render(desenho);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(rtb));
            await using var arquivo = File.Create(Path.Combine(pasta, nome + ".png"));
            png.Save(arquivo);
        }

        try
        {
            _preparando = true;
            _fracaoPreparo = 0.42;
            _mbPreparo = 157;
            AtualizarBotao();
            AtualizarStatus();
            await Salvar("1-preparando");

            _preparando = false;
            _fracaoPreparo = null;
            _prontos = true;
            DefinirLink("https://exemplo.com/musica");
            await Salvar("2-link-invalido");

            DefinirLink(Environment.GetEnvironmentVariable("FAIXA_CAPTURA_LINK") ?? "https://youtu.be/dQw4w9WgXcQ");
            await Salvar("3-carregando");
            for (var i = 0; i < 150 && _idCarregando is not null; i++)
                await Task.Delay(100);
            await Salvar("4-previa");

            // Análise com um JSON no formato do yt-dlp -J: Opus 129,5 e AAC 129,6 na mesma faixa
            // de qualidade (deve vencer o Opus), uma versão DRC e formatos de vídeo (devem sumir).
            _analisando = true;
            MostrarFonte();
            AtualizarBotao();
            AtualizarStatus();
            await Salvar("5-analisando");

            _audio = Analisador.Interpretar(JsonDeExemplo);
            File.WriteAllText(Path.Combine(pasta, "analise.txt"),
                $"melhor={_audio?.Melhor}\noriginais={string.Join(" | ", _audio?.Originais ?? [])}\nduracao={_audio?.Duracao}\n");
            _analisando = false;
            MontarOpcoes();
            MostrarFonte();
            AtualizarBotao();
            AtualizarStatus();
            await Salvar("6-analisado");

            // Mesmo vídeo como se o YouTube informasse os dados da música (canal Tópico, por exemplo).
            DefinirNome(Metadados.Resolver(new DadosMusica("x", "x", Faixa: "Never Gonna Give You Up",
                Artistas: "Rick Astley", Album: "Whenever You Need Somebody", Ano: 1987)));
            await Salvar("6b-oficial");
            _nomeEditado = true;
            DefinirNome(new NomeMusica("Rick Astley", "Never Gonna Give You Up (Ao Vivo)", null, null, OrigemNome.Editado));
            await Salvar("6c-editado");

            var popup = (System.Windows.Controls.Primitives.Popup)SeletorFormato.Template.FindName("PART_Popup", SeletorFormato);
            SeletorFormato.IsDropDownOpen = true;
            await Task.Delay(400);
            if (popup.Child is FrameworkElement lista)
                await SalvarElemento(lista, "7-opcoes");
            SeletorFormato.IsDropDownOpen = false;

            SeletorFormato.SelectedItem = _opcoes.First(o => o.Chave == "original-opus");
            await Salvar("8-original");
            SeletorFormato.SelectedItem = _opcoes.First(o => o.Chave == "mp3-128");
            await Salvar("9-mp3-128");
            SeletorFormato.SelectedItem = _opcoes.First(o => o.Chave == Formatos.Padrao);

            _baixando = true;
            _opcaoEmAndamento = OpcaoAtual;
            AtualizarBotao();
            MostrarProgresso(new Progresso(Fase.Baixando, 0.62));
            await Salvar("10-baixando");
            MostrarProgresso(new Progresso(Fase.Convertendo));
            await Salvar("11-convertendo");

            _baixando = false;
            _baixada = true;
            _avisoResultado = new Aviso("Salva em Downloads › Músicas. "
                + DescreverResultado(OpcaoAtual, new Resultado("x.mp3", "opus", 129.5)) + ".", Tom.Sucesso, "Abrir pasta");
            AtualizarBotao();
            AtualizarStatus();
            await Salvar("12-concluido");

            _baixada = false;
            _avisoResultado = new Aviso("O YouTube pediu verificação anti-robô. Tente daqui a alguns minutos.", Tom.Erro);
            AtualizarBotao();
            AtualizarStatus();
            await Salvar("13-erro");

            // Playlist (JSON no formato do --flat-playlist), com uma faixa privada.
            _avisoResultado = null;
            _link = new LinkYouTube("dQw4w9WgXcQ", "PLcapturasfaixa");
            ZerarTudo();
            MostrarModoLista(true);
            var exemplo = Playlists.Interpretar(JsonPlaylistDeExemplo, "PLcapturasfaixa")!;
            MostrarLista(exemplo);
            await CarregarCapaDaListaAsync(exemplo, CancellationToken.None);
            AtualizarSelecao();
            AtualizarStatus();
            await Salvar("14-playlist");

            _faixas[2].Selecionada = false;
            AtualizarSelecao();
            AtualizarStatus();
            await Salvar("15-playlist-desmarcada");
            _faixas[2].Selecionada = true;

            _baixando = true;
            _opcaoEmAndamento = OpcaoAtual;
            AtualizarSelecao();
            _faixas[0].Estado = EstadoFaixa.Pronta;
            _faixas[0].Nome = new NomeMusica("Rick Astley", "Never Gonna Give You Up", null, null, OrigemNome.Oficial);
            _faixas[2].Estado = EstadoFaixa.Falhou;
            _faixas[2].Detalhe = "Vídeo com restrição de idade. O YouTube só libera com login.";
            _faixas[3].Estado = EstadoFaixa.Baixando;
            MostrarProgressoLista(_faixas[3], 2, 6, new Progresso(Fase.Baixando, 0.48));
            await Salvar("16-playlist-baixando");

            _baixando = false;
            foreach (var f in _faixas.Where(f => f.Item.Disponivel && f.Estado != EstadoFaixa.Falhou))
                f.Estado = EstadoFaixa.Pronta;
            _avisoResultado = new Aviso("5 de 6 salvas. 1 falhou: passe o mouse na faixa para ver o motivo.", Tom.Erro, "Abrir pasta");
            AtualizarSelecao();
            AtualizarStatus();
            await Salvar("17-playlist-concluida");

            BotaoAtualizarTexto.Text = "Versão 1.2";
            BotaoAtualizar.Visibility = Visibility.Visible;
            await Salvar("18-versao-nova");
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
}
