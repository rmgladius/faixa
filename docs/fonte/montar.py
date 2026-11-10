"""Monta as imagens do README a partir dos quadros do `Faixa.exe --demo <pasta>`.

Uso: python3 docs/fonte/montar.py <pasta dos quadros> <docs/imagens>

Gera demo.webp (animação), capa.png (topo do README e prévia do GitHub, 1280x640) e telas.png.
Precisa das fontes Segoe UI do Windows (lidas de C:\\Windows\\Fonts pelo WSL).
"""
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

quadros_dir = Path(sys.argv[1])
destino = Path(sys.argv[2])
destino.mkdir(parents=True, exist_ok=True)
icone_png = Path(__file__).resolve().parents[2] / "src/Faixa/Assets/faixa.png"

FONTES = Path("/mnt/c/Windows/Fonts")
FUNDO = (15, 17, 21)
SUPERFICIE2 = (32, 36, 45)
BORDA = (42, 46, 55)
TEXTO = (242, 244, 248)
SUAVE = (154, 163, 178)
DESTAQUE = (255, 106, 61)
E = 2  # os quadros vêm em escala 2


def fonte(nome, tam):
    return ImageFont.truetype(str(FONTES / nome), tam)


def variavel(tam, peso):
    f = ImageFont.truetype(str(FONTES / "SegUIVar.ttf"), tam)
    try:
        f.set_variation_by_axes([peso])
    except Exception:
        pass
    return f


def cantos(img, raio):
    mascara = Image.new("L", img.size, 0)
    ImageDraw.Draw(mascara).rounded_rectangle([0, 0, img.width - 1, img.height - 1], raio, fill=255)
    saida = img.copy()
    saida.putalpha(ImageChops.multiply(saida.getchannel("A"), mascara))
    return saida


def sombra(tam, raio, desfoque, opacidade):
    """Sombra (RGBA) do tamanho `tam`, para ficar atrás de um retângulo arredondado."""
    s = Image.new("L", (tam[0] + desfoque * 4, tam[1] + desfoque * 4), 0)
    ImageDraw.Draw(s).rounded_rectangle([desfoque * 2, desfoque * 2, desfoque * 2 + tam[0], desfoque * 2 + tam[1]], raio,
                                        fill=int(255 * opacidade))
    s = s.filter(ImageFilter.GaussianBlur(desfoque))
    preto = Image.new("RGBA", s.size, (0, 0, 0, 0))
    preto.putalpha(s)
    return preto


def com_popup(quadro):
    """Sobrepõe a lista aberta do seletor de formato, com a sombra dela."""
    popup = Image.open(quadros_dir / "popup.png").convert("RGBA")
    x, y = (int(v) for v in (quadros_dir / "popup.txt").read_text().split())
    saida = quadro.copy()
    s = sombra(popup.size, 24, 20, 0.5)
    saida.alpha_composite(s, (x - 40, y - 40 + 8))
    saida.alpha_composite(popup, (x, y))
    return saida


def barra_de_titulo(largura):
    """Barra de título do Windows 11 na cor do Faixa (o app pinta a dele de #0F1115)."""
    h = 32 * E
    barra = Image.new("RGBA", (largura, h), FUNDO + (255,))
    d = ImageDraw.Draw(barra)
    ico = Image.open(icone_png).convert("RGBA").resize((16 * E, 16 * E), Image.LANCZOS)
    barra.alpha_composite(ico, (12 * E, 8 * E))
    d.text((38 * E, 16 * E), "Faixa", font=fonte("segoeui.ttf", 12 * E), fill=(200, 204, 212), anchor="lm")
    glifos = fonte("SegoeIcons.ttf", 10 * E)
    for i, (glifo, cor) in enumerate((("\uE8BB", (220, 222, 228)), ("\uE922", (90, 94, 102)), ("\uE921", (220, 222, 228)))):
        cx = largura - (23 + 46 * i) * E
        d.text((cx, 16 * E), glifo, font=glifos, fill=cor, anchor="mm")
    return barra


def janela(quadro):
    """Quadro + barra de título, cantos de 8 px e borda fina, como o Windows 11 desenha."""
    quadro = quadro.convert("RGBA")
    barra = barra_de_titulo(quadro.width)
    j = Image.new("RGBA", (quadro.width, quadro.height + barra.height), FUNDO + (255,))
    j.alpha_composite(barra, (0, 0))
    j.alpha_composite(quadro, (0, barra.height))
    j = cantos(j, 8 * E)
    ImageDraw.Draw(j).rounded_rectangle([0, 0, j.width - 1, j.height - 1], 8 * E, outline=BORDA + (255,), width=E)
    return j


def ler_quadros():
    for linha in (quadros_dir / "quadros.txt").read_text().splitlines():
        nome, ms, marca = linha.split()
        img = Image.open(quadros_dir / f"{nome}.png").convert("RGBA")
        if marca == "popup":
            img = com_popup(img)
        yield img, int(ms), marca


quadros = list(ler_quadros())
por_marca = {marca: img for img, _, marca in quadros if marca != "-"}

# ── Animação: janela com sombra sobre fundo transparente, ~1,5x para ficar nítida em telas densas
LARGURA_DEMO = 640
margem = 36
amostra = janela(quadros[0][0])
escala = LARGURA_DEMO / amostra.width
tam_janela = (LARGURA_DEMO, round(amostra.height * escala))
base = Image.new("RGBA", (tam_janela[0] + 2 * margem, tam_janela[1] + 2 * margem), (0, 0, 0, 0))
s = sombra(tam_janela, 12, 18, 0.55)
base.alpha_composite(s, (margem - 36, margem - 36 + 10))

imagens, duracoes = [], []
for img, ms, _ in quadros:
    q = base.copy()
    q.alpha_composite(janela(img).resize(tam_janela, Image.LANCZOS), (margem, margem))
    imagens.append(q)
    duracoes.append(ms)
imagens[0].save(destino / "demo.webp", save_all=True, append_images=imagens[1:], duration=duracoes,
                loop=0, quality=90, method=6)

# ── Telas lado a lado: uma música, formato, playlist
telas = [("Uma música", por_marca["analise"]), ("Escolha do formato", por_marca["popup"]),
         ("Playlist", por_marca["playlist-baixando"])]
LARG_TELA = 400
alt_tela = round(janela(telas[0][1]).height * LARG_TELA / janela(telas[0][1]).width)
espaco, topo, rodape = 48, 48, 92
painel = Image.new("RGBA", (3 * LARG_TELA + 4 * espaco, topo + alt_tela + rodape), (0, 0, 0, 0))
ImageDraw.Draw(painel).rounded_rectangle([0, 0, painel.width - 1, painel.height - 1], 28, fill=(11, 12, 16, 255))
legenda = variavel(26, 600)
for i, (titulo, img) in enumerate(telas):
    x = espaco + i * (LARG_TELA + espaco)
    j = janela(img).resize((LARG_TELA, alt_tela), Image.LANCZOS)
    painel.alpha_composite(sombra(j.size, 10, 14, 0.6), (x - 28, topo - 28 + 8))
    painel.alpha_composite(j, (x, topo))
    ImageDraw.Draw(painel).text((x + LARG_TELA // 2, topo + alt_tela + 46), titulo, font=legenda, fill=SUAVE, anchor="mm")
painel.save(destino / "telas.png", optimize=True)

# ── Capa (1280x640, desenhada em 2x e reduzida)
K = 2
W, H = 1280 * K, 640 * K
capa = Image.new("RGBA", (W, H), FUNDO + (255,))
brilho = Image.new("RGBA", (W, H), (0, 0, 0, 0))
db = ImageDraw.Draw(brilho)
db.ellipse([-300 * K, 260 * K, 700 * K, 1000 * K], fill=DESTAQUE + (70,))
db.ellipse([820 * K, -380 * K, 1500 * K, 260 * K], fill=(240, 74, 110, 46))
capa.alpha_composite(brilho.filter(ImageFilter.GaussianBlur(170 * K)))
ruido = Image.effect_noise((W, H), 24).convert("L").point(lambda v: int(v * 0.05))
capa.alpha_composite(Image.merge("RGBA", (ruido, ruido, ruido, ruido)))

# Janelas: a da playlist atrás, a da música na frente, saindo pela borda de baixo
def janela_capa(img, largura):
    j = janela(img)
    return j.resize((largura, round(j.height * largura / j.width)), Image.LANCZOS)

tras = janela_capa(por_marca["playlist"], 360 * K)
tras_escura = Image.new("RGBA", tras.size, (0, 0, 0, 0))
tras_escura.alpha_composite(tras)
tras_escura.alpha_composite(Image.new("RGBA", tras.size, (8, 9, 12, 70)))
tras_escura.putalpha(tras.getchannel("A"))
frente = janela_capa(por_marca["pronto"], 400 * K)
for img, (x, y) in ((tras_escura, (898 * K, 92 * K)), (frente, (700 * K, 56 * K))):
    capa.alpha_composite(sombra(img.size, 16 * K, 30 * K, 0.6), (x - 60 * K, y - 60 * K + 18 * K))
    capa.alpha_composite(img, (x, y))

d = ImageDraw.Draw(capa)
ico = Image.open(icone_png).convert("RGBA").resize((60 * K, 60 * K), Image.LANCZOS)
capa.alpha_composite(ico, (84 * K, 112 * K))
d.text((158 * K, 142 * K), "Faixa", font=variavel(38 * K, 600), fill=TEXTO, anchor="lm")
titulo = variavel(58 * K, 650)
d.text((84 * K, 232 * K), "Música do YouTube", font=titulo, fill=TEXTO, anchor="ls")
d.text((84 * K, 302 * K), "sem 320 kbps", font=titulo, fill=DESTAQUE, anchor="ls")
d.text((84 * K, 372 * K), "de mentira.", font=titulo, fill=DESTAQUE, anchor="ls")
texto = variavel(21 * K, 400)
for i, linha in enumerate(("Mostra a qualidade real do áudio antes de baixar,",
                           "limpa o nome das músicas e baixa playlists inteiras.")):
    d.text((86 * K, (428 + i * 32) * K), linha, font=texto, fill=SUAVE, anchor="ls")
x = 84 * K
pilula = variavel(16 * K, 500)
for rotulo in ("Windows 10 e 11", "C# · WPF · .NET 10", "yt-dlp + FFmpeg"):
    largura = d.textlength(rotulo, font=pilula) + 32 * K
    d.rounded_rectangle([x, 506 * K, x + largura, 542 * K], 18 * K, fill=SUPERFICIE2 + (255,))
    d.text((x + largura / 2, 524 * K), rotulo, font=pilula, fill=(214, 218, 226), anchor="mm")
    x += largura + 10 * K

capa.convert("RGB").resize((1280, 640), Image.LANCZOS).save(destino / "capa.png", optimize=True)

for nome in ("demo.webp", "telas.png", "capa.png"):
    print(nome, round((destino / nome).stat().st_size / 1024), "KB")
