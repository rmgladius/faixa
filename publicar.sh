#!/usr/bin/env bash
# Publica uma versão do RD Track Downloader no GitHub Releases (roda no WSL, usa o .NET do Windows).
#
#   1. Suba a <Version> em Directory.Build.props e faça o commit.
#   2. ./publicar.sh "- Novidade 1
#      - Novidade 2"
#
# Gera distribuir/RDTrackDownloader.exe (autossuficiente), o .sha256 dele e RDTrackDownloader.zip
# (exe + LEIA-ME) e cria a release vX.Y.Z. O botão de atualização procura o asset RDTrackDownloader.exe;
# vai junto uma cópia como Faixa.exe, o nome que a versão 1.1 procura, para ela também se atualizar.
set -euo pipefail
cd "$(dirname "$0")"

notas="${1:?Passe as novidades da versão entre aspas.}"
repo="rmgladius/faixa"
versao=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
tag="v$versao"
win=$(wslpath -w "$PWD")

[ "$(git branch --show-current)" = "main" ] || { echo "Publique a partir da main."; exit 1; }
git diff --quiet && git diff --cached --quiet || { echo "Há mudanças sem commit."; exit 1; }
if gh release view "$tag" --repo "$repo" >/dev/null 2>&1; then
  echo "A release $tag já existe. Suba a versão em Directory.Build.props."; exit 1
fi

echo "== Testes"
cmd.exe /c "cd /d $win && dotnet test Faixa.slnx -c Release --nologo -v q"

echo "== Executável autossuficiente $versao"
rm -rf distribuir
cmd.exe /c "cd /d $win && dotnet publish src\\Faixa\\Faixa.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o distribuir -nologo -v q"

exe=RDTrackDownloader
sha256sum distribuir/$exe.exe | cut -d' ' -f1 > distribuir/$exe.exe.sha256
cp distribuir/$exe.exe distribuir/Faixa.exe
python3 -I - <<'PY'
import zipfile
leia = open('distribuicao/LEIA-ME.txt', encoding='utf-8').read().replace('\n', '\r\n')
with zipfile.ZipFile('distribuir/RDTrackDownloader.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    z.write('distribuir/RDTrackDownloader.exe', 'RDTrackDownloader.exe')
    z.writestr('LEIA-ME.txt', leia.encode('utf-8-sig'))
PY

echo "== Release $tag"
git push origin main
gh release create "$tag" distribuir/$exe.exe distribuir/$exe.exe.sha256 distribuir/$exe.zip distribuir/Faixa.exe \
  --repo "$repo" --title "RD Track Downloader $versao" --notes "$notas"
echo "Pronto: https://github.com/$repo/releases/tag/$tag"
