using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Faixa.Servicos;

namespace Faixa;

public enum EstadoFaixa { Aguardando, Baixando, Pronta, Falhou, Indisponivel }

/// <summary>Uma linha da lista de faixas: o que mostrar e em que pé está o download dela.</summary>
public sealed class FaixaDaLista : INotifyPropertyChanged
{
    private NomeMusica _nome;
    private bool _selecionada;
    private EstadoFaixa _estado;
    private double _percentual;
    private string? _detalhe;

    public FaixaDaLista(ItemLista item)
    {
        Item = item;
        _nome = Metadados.Resolver(new DadosMusica(item.Titulo, item.Canal));
        _estado = item.Disponivel ? EstadoFaixa.Aguardando : EstadoFaixa.Indisponivel;
        _selecionada = item.Disponivel;
        _detalhe = item.Disponivel ? null : "Vídeo privado ou apagado: não dá para baixar.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ItemLista Item { get; }

    public int Numero => Item.Numero;

    public NomeMusica Nome
    {
        get => _nome;
        set
        {
            _nome = value;
            Avisar(nameof(Nome), nameof(Musica), nameof(Artista));
        }
    }

    public string Musica => Nome.Musica;

    public string Artista => Nome.Artista;

    public bool Selecionada
    {
        get => _selecionada;
        set
        {
            if (_selecionada == value)
                return;
            _selecionada = value;
            Avisar(nameof(Selecionada));
        }
    }

    public EstadoFaixa Estado
    {
        get => _estado;
        set
        {
            _estado = value;
            Avisar(nameof(Estado), nameof(Situacao), nameof(Icone), nameof(Cor), nameof(PodeMarcar), nameof(Opacidade));
        }
    }

    public double Percentual
    {
        get => _percentual;
        set
        {
            _percentual = value;
            Avisar(nameof(Situacao));
        }
    }

    /// <summary>Dica ao passar o mouse: a qualidade de origem quando termina, ou o motivo da falha.</summary>
    public string? Detalhe
    {
        get => _detalhe;
        set
        {
            _detalhe = value;
            Avisar(nameof(Detalhe));
        }
    }

    /// <summary>Precisa ser baixada: marcada, disponível e ainda não pronta.</summary>
    public bool Pendente => Selecionada && Item.Disponivel && Estado != EstadoFaixa.Pronta;

    public bool PodeMarcar => Item.Disponivel && Estado != EstadoFaixa.Baixando;

    public double Opacidade => Item.Disponivel ? 1 : 0.45;

    public string Situacao => Estado switch
    {
        EstadoFaixa.Baixando => $"{(int)(Percentual * 100)}%",
        EstadoFaixa.Pronta => "",
        EstadoFaixa.Falhou => "falhou",
        EstadoFaixa.Indisponivel => "indisponível",
        _ => Item.Duracao is double d ? Formatos.Duracao(d) : "",
    };

    public string Icone => Estado switch
    {
        EstadoFaixa.Pronta => "",
        EstadoFaixa.Falhou => "",
        _ => "",
    };

    public Brush Cor => (Brush)Application.Current.FindResource(Estado switch
    {
        EstadoFaixa.Pronta => "Sucesso",
        EstadoFaixa.Falhou => "Erro",
        EstadoFaixa.Baixando => "Destaque",
        _ => "TextoSuave",
    });

    private void Avisar(params string[] nomes)
    {
        foreach (var nome in nomes)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
    }
}
