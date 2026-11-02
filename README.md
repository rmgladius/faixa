# Faixa

Baixa músicas do YouTube em MP3 (até 320 kbps) ou no formato original. Cola o link, a capa e a
qualidade real do áudio aparecem, escolhe o formato e clica em **Baixar**.

- Salva em **Downloads › Músicas** por padrão (dá para trocar em "Alterar"; a escolha fica gravada).
- Mostra a qualidade real do áudio no YouTube antes de baixar ("No YouTube: Opus · 130 kbps · 48 kHz").
- Formato à escolha: MP3 320, 256, 192 ou 128 kbps, ou o original sem conversão (.opus ou .m4a),
  com o tamanho estimado de cada um. A escolha fica gravada.
- Embaixo do botão, uma frase diz o que a escolha faz com o áudio (ex.: MP3 320 a partir de
  Opus 130 não fica melhor que a fonte, só perde menos na conversão).
- Depois de baixar, informa o formato que o yt-dlp realmente baixou ("Opus 130 kbps → MP3 320 kbps").
- O arquivo sai com título, artista, data e a capa do vídeo cortada em quadrado.
- Aceita links `youtube.com/watch`, `youtu.be`, `music.youtube.com`, `shorts`, `live` e `embed`.
  Link de playlist baixa só o vídeo aberto.
- Atalhos: Ctrl+V em qualquer lugar da janela cola o link; Enter baixa; dá para arrastar o link do navegador.

## Como funciona

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
       --no-playlist -o "%(title)s.%(ext)s" <link>   (MP3; no original: --audio-format opus ou m4a)
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
- `%LOCALAPPDATA%\Faixa\config.json`: pasta de destino escolhida
- `%LOCALAPPDATA%\Faixa\ultimo-erro.txt`: saída do yt-dlp do último erro (botão "Detalhes")

## Compilar

Precisa do .NET 10 SDK.

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish
```

## Compartilhar

A versão acima depende do .NET 10 instalado. Para mandar a quem não tem, gere a autossuficiente
(um `.exe` de ~60 MB com o .NET dentro, sem instalador):

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o distribuir
```

`distribuir\Faixa.zip` leva o `.exe` e um `LEIA-ME.txt` com os passos (inclusive o aviso do
SmartScreen, que aparece por o programa não ter assinatura digital).

`Faixa.exe --capturas <pasta>` desenha cada estado da tela em PNG e fecha, sem baixar nada.
