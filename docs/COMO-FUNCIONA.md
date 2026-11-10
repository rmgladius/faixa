# Como o Faixa funciona

Notas técnicas. A apresentação está no [README](../README.md).

## Componentes e comandos

O Faixa é só a interface. Quem faz o trabalho são três programas que ele baixa sozinho na
primeira vez que abre (cerca de 157 MB, em `%LOCALAPPDATA%\Faixa\bin`):

| Componente | Para quê |
|---|---|
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | Busca o melhor áudio do vídeo |
| [FFmpeg](https://github.com/yt-dlp/FFmpeg-Builds) (build do yt-dlp) | Converte para MP3 320 kbps e grava capa e tags |
| [Deno](https://github.com/denoland/deno) | O yt-dlp precisa dele desde a versão 2025.11 para o YouTube |

O yt-dlp se atualiza a cada dois dias (`yt-dlp -U`). Se um download falha por motivo
desconhecido, o Faixa atualiza e tenta mais uma vez antes de mostrar o erro.

O comando que roda por baixo é, em resumo:

```
yt-dlp -J <link>                                    (análise: lista os formatos, não baixa)
yt-dlp -f 251/bestaudio/best -x --audio-format mp3 --audio-quality 320K
       --embed-metadata --embed-thumbnail --convert-thumbnails jpg
       --no-playlist -o "%(title)s.%(ext)s" <link>   (MP3; no original: --audio-format best)
yt-dlp --flat-playlist -J <playlist>                (playlist: só a lista de faixas)
```

Como o "melhor áudio" é escolhido: o Faixa roda `yt-dlp -J` (só lê, não baixa), descarta
formatos com vídeo, DRC (volume comprimido), DRM ou defeito, fica com o idioma original e ordena
como o yt-dlp: faixa de qualidade do YouTube, codec (Opus > Vorbis > AAC) e kbps. O download pede
esse formato pelo ID, então o que a tela mostra é o que é baixado.

Sobre os 320 kbps: o YouTube entrega o áudio em Opus (~130 a 160 kbps) ou AAC (128 kbps).
O MP3 sai em 320 kbps fixos, que é a forma de perder o mínimo na conversão, mas não fica
com mais qualidade que a original.

## Acesso controlado a pastas

Com a proteção contra ransomware do Windows ligada, Documentos, Músicas, Imagens, Vídeos e
Área de Trabalho só aceitam gravação de apps permitidos. Downloads fica de fora, por isso a
pasta padrão é Downloads › Músicas e o Faixa nunca pede liberação.

O yt-dlp trabalha só em `%LOCALAPPDATA%\Faixa\temp`, e quem move o MP3 pronto para o destino
é o próprio Faixa. Se a pessoa escolher uma pasta protegida, o Faixa avisa na hora ("Usar
Downloads"); se a gravação for bloqueada, o MP3 fica guardado e o botão "Salvar em Downloads"
o entrega sem baixar de novo. MP3 que tenha ficado pendente é entregue ao abrir o Faixa.

## Arquivos

- `%LOCALAPPDATA%\Programs\Faixa\Faixa.exe`: o programa instalado
- `%LOCALAPPDATA%\Faixa\config.json`: pasta de destino e formato escolhidos
- `%LOCALAPPDATA%\Faixa\ultimo-erro.txt`: saída do yt-dlp do último erro (botão "Detalhes")

## Projeto

```
src/Faixa/          app WPF (tela)
src/Faixa.Nucleo/   lógica: links, yt-dlp, formatos, nomes, tags, playlists, atualização
tests/Faixa.Testes/ testes xUnit do núcleo
distribuicao/       LEIA-ME que vai no zip para os amigos
publicar.sh         gera e publica uma versão no GitHub Releases
```

Precisa do .NET 10 SDK. Comandos a partir da raiz:

```
dotnet test Faixa.slnx -c Release
dotnet publish src\Faixa\Faixa.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish
```

`Faixa.exe --capturas <pasta>` desenha cada estado da tela em PNG e fecha, sem baixar nada.
`Faixa.exe --demo <pasta>` gera os quadros da animação do README com músicas e capas fictícias
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

## Atualização

O Faixa consulta `api.github.com/repos/rmgladius/faixa/releases/latest` ao abrir. Se a versão for
maior que a dele, aparece um botão no cabeçalho; ao clicar, ele baixa o asset `Faixa.exe`, confere
o SHA-256 (campo `digest` do GitHub ou `Faixa.exe.sha256`), renomeia o .exe atual para `.old`,
põe o novo no lugar e reabre. Se a pasta não deixar trocar, o .exe novo vai para Downloads.

Para publicar: suba `<Version>` em `Directory.Build.props`, faça o commit e rode
`./publicar.sh "- novidade"` no WSL. Para mandar o Faixa a alguém, o link é a página da última
release; o `Faixa.zip` dela leva o .exe e o `LEIA-ME.txt` de `distribuicao/`.
