# Como o RD Track Downloader funciona

Notas técnicas. A apresentação está no [README](../README.md). No código o projeto se chama Faixa
(pastas, solução e namespace), o nome que o programa teve até a versão 1.1.

## Componentes e comandos

O app é só a interface. Quem faz o trabalho são três programas que ele baixa sozinho na
primeira vez que abre (cerca de 157 MB, em `%LOCALAPPDATA%\RD Track Downloader\bin`):

| Componente | Para quê |
|---|---|
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | Busca o melhor áudio do vídeo |
| [FFmpeg](https://github.com/yt-dlp/FFmpeg-Builds) (build do yt-dlp) | Converte para MP3 320 kbps e grava capa e tags |
| [Deno](https://github.com/denoland/deno) | O yt-dlp precisa dele desde a versão 2025.11 para o YouTube |

O yt-dlp se atualiza a cada dois dias (`yt-dlp -U`). Se um download falha por motivo
desconhecido, o app atualiza o yt-dlp e tenta mais uma vez antes de mostrar o erro.

O comando que roda por baixo é, em resumo:

```
yt-dlp -J <link>                                    (análise: lista os formatos, não baixa)
yt-dlp -f 251/bestaudio/best -x --audio-format mp3 --audio-quality 320K
       --embed-metadata --embed-thumbnail --convert-thumbnails jpg
       --no-playlist -o "%(title)s.%(ext)s" <link>   (MP3; no original: --audio-format best)
yt-dlp --flat-playlist -J <playlist>                (playlist: só a lista de faixas)
```

Como o "melhor áudio" é escolhido: o app roda `yt-dlp -J` (só lê, não baixa), descarta
formatos com vídeo, DRC (volume comprimido), DRM ou defeito, fica com o idioma original e ordena
como o yt-dlp: faixa de qualidade do YouTube, codec (Opus > Vorbis > AAC) e kbps. O download pede
esse formato pelo ID, então o que a tela mostra é o que é baixado.

Sobre os 320 kbps: o YouTube entrega o áudio em Opus (~130 a 160 kbps) ou AAC (128 kbps).
O MP3 sai em 320 kbps fixos, que é a forma de perder o mínimo na conversão, mas não fica
com mais qualidade que a original.

## Acesso controlado a pastas

Com a proteção contra ransomware do Windows ligada, Documentos, Músicas, Imagens, Vídeos e
Área de Trabalho só aceitam gravação de apps permitidos. Downloads fica de fora, por isso a
pasta padrão é Downloads › Músicas e o app nunca pede liberação.

O yt-dlp trabalha só em `%LOCALAPPDATA%\RD Track Downloader\temp`, e quem move o MP3 pronto para o
destino é o próprio app. Se a pessoa escolher uma pasta protegida, o app avisa na hora ("Usar
Downloads"); se a gravação for bloqueada, o MP3 fica guardado e o botão "Salvar em Downloads"
o entrega sem baixar de novo. MP3 que tenha ficado pendente é entregue ao abrir o app.

## Arquivos

- `RDTrackDownloader.exe`: o programa, na pasta que a pessoa escolheu
- `%LOCALAPPDATA%\RD Track Downloader\config.json`: pasta de destino e formato escolhidos
- `%LOCALAPPDATA%\RD Track Downloader\ultimo-erro.txt`: saída do yt-dlp do último erro (botão "Detalhes")

Até a versão 1.1 a pasta interna era `%LOCALAPPDATA%\Faixa`. Na primeira abertura, o que estiver lá
(componentes, configuração, músicas por entregar) passa para a pasta nova.

## Projeto

```
src/Faixa/          app WPF (tela)
src/Faixa.Nucleo/   lógica: links, yt-dlp, formatos, nomes, tags, playlists, atualização,
                    Spotify, Deezer, SoundCloud e busca no YouTube Music
tests/Faixa.Testes/ testes xUnit do núcleo (Exemplos/: respostas reais reduzidas dos serviços)
distribuicao/       LEIA-ME que vai no zip para os amigos
publicar.sh         gera e publica uma versão no GitHub Releases
```

Precisa do .NET 10 SDK. Comandos a partir da raiz:

```
dotnet test Faixa.slnx -c Release
dotnet publish src\Faixa\Faixa.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish
```

`RDTrackDownloader.exe --capturas <pasta>` desenha cada estado da tela em PNG e fecha, sem baixar nada.
`RDTrackDownloader.exe --demo <pasta>` gera os quadros da animação do README com músicas e capas fictícias
(`docs/fonte/capas.py` faz as capas e `docs/fonte/montar.py` monta as imagens).

## Nomes e informações da música

1. **Dados oficiais do YouTube**, quando existem: faixas do YouTube Music (canais "Tópico") e
   vídeos em que o YouTube reconhece a música e mostra a seção "Música" na descrição.
2. **Separados do título**: "Artista - Música", "Música - Artista" (quando o lado direito é o
   canal) e 'Artista "Música"', sem "(Official Video)", "[4K]", "(Lyrics)", hashtags e afins.
   "(Ao Vivo)", "(Remastered 2011)" e "feat." ficam, porque dizem qual versão é.
3. **Canal como artista**, quando o título não tem separação ("RickAstleyVEVO" vira "Rick Astley").

A tela diz qual dos três foi usado e deixa editar. O arquivo sai como `Artista - Música.mp3`, e
artista, título, álbum, ano e número da faixa (em álbuns) vão para as tags (ID3v2.3, via TagLib#).

## Playlists

Links de playlist, álbum do YouTube Music (`OLAK5uy_…`, `browse/MPREb…`) e vídeo aberto dentro de
uma playlist ("Playlist inteira" / "Só esta música"). Mixes automáticos (`list=RD…`) contam só como o
vídeo. As faixas marcadas são baixadas uma por vez numa subpasta com o nome da playlist; álbuns
ganham "01. " no nome e o número da faixa nas tags. Vídeos privados e apagados ficam desmarcados.

## Outras plataformas

Os links são reconhecidos em `Links.cs`. Links curtos dos apps (`spotify.link`, `link.deezer.com`,
`deezer.page.link`, `on.soundcloud.com`) são abertos para ver aonde levam. Perfil, artista e podcast
ganham uma mensagem dizendo o que colar no lugar.

**SoundCloud.** O yt-dlp baixa o áudio, como no YouTube. A prévia vem do oEmbed público
(`soundcloud.com/oembed`). A lista de um set vem do JSON que a própria página traz
(`window.__sc_hydration`): as cinco primeiras faixas vêm completas e as outras só com o código, que o
oEmbed completa. Se a página mudar, cai na lista do yt-dlp (`--flat-playlist`), que traz só os
endereços. Faixas com política `SNIP` (Go+) ficam indisponíveis, e na análise os formatos `_preview`
são descartados: só o trecho de 30 s não vale um download.

**Spotify.** Os dados vêm da página de incorporação pública (`open.spotify.com/embed/...`, o player
que sites põem nas páginas), do JSON `__NEXT_DATA__`. Não precisa de conta nem de chave e inclui as
playlists do próprio Spotify. Limites: playlists mostram até 100 faixas e não vem o ISRC.

**Deezer.** API pública (`api.deezer.com`), sem chave. Traz o ISRC de cada música. Listas longas
vêm em páginas de 100, até 2.000 faixas.

**Busca no YouTube Music.** `YouTubeMusic.cs` usa a mesma busca do site (`youtubei/v1/search`,
cliente `WEB_REMIX`, filtro "Músicas"), que devolve título, artistas, álbum e duração de cada
resultado. Se um dia ela recusar o pedido, o primeiro suspeito é a constante `VersaoCliente`.

**Qual resultado vale** (`Correspondencia.cs`). Primeiro a busca pelo ISRC, quando existe; depois por
artista e nome. Cada resultado ganha uma nota de 0 a 1:

- nome sem a versão ("Here Comes The Sun - Remastered 2009" e "Here Comes The Sun (Remastered 2009)"
  viram "Here Comes The Sun"), 40%;
- artista, 30%: inteiro vale tudo, só o sobrenome vale metade ("Tom Jobim" e "Antonio Carlos Jobim");
- duração, 30%: até 3 s de diferença vale tudo; acima de 30 s é outra gravação e a nota zera;
- menos 0,4 se só um lado tem ao vivo, acústico, remix, instrumental, cover, karaokê, sped up e afins
  (no nome ou no álbum);
- menos até 0,15 se a edição difere ("2022 Remaster" contra "Remastered 2009").

Abaixo de 0,7, nada é baixado e a tela oferece abrir a busca no navegador. O arquivo sai com nome,
artistas, álbum e capa do Spotify ou do Deezer; o álbum do YouTube Music só entra quando o serviço não
informa (playlists do Spotify).

## Atualização

O app consulta `api.github.com/repos/rmgladius/faixa/releases/latest` ao abrir. Se a versão for
maior que a dele, aparece um botão no cabeçalho; ao clicar, ele baixa o asset `RDTrackDownloader.exe`,
confere o SHA-256 (campo `digest` do GitHub ou `RDTrackDownloader.exe.sha256`), renomeia o .exe atual
para `.old`, põe o novo no lugar e reabre. Se a pasta não deixar trocar, o .exe novo vai para Downloads.
Cada release leva também uma cópia como `Faixa.exe`, o nome que a versão 1.1 procura.

Para publicar: suba `<Version>` em `Directory.Build.props`, faça o commit e rode
`./publicar.sh "- novidade"` no WSL. Para mandar o programa a alguém, o link é a página da última
release; o `RDTrackDownloader.zip` dela leva o .exe e o `LEIA-ME.txt` de `distribuicao/`.
