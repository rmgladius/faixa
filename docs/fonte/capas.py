"""Capas fictícias (1280x720, arte no quadrado central) para a demonstração do Faixa.

Uso: python3 docs/fonte/capas.py <pasta de saída>
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

L, A = 1280, 720
saida = Path(sys.argv[1])
saida.mkdir(parents=True, exist_ok=True)


def degrade(c1, c2, tam=(L, A), vertical=True):
    img = Image.new("RGB", tam)
    d = ImageDraw.Draw(img)
    n = tam[1] if vertical else tam[0]
    for i in range(n):
        t = i / (n - 1)
        cor = tuple(int(c1[k] + (c2[k] - c1[k]) * t) for k in range(3))
        if vertical:
            d.line([(0, i), (tam[0], i)], fill=cor)
        else:
            d.line([(i, 0), (i, tam[1])], fill=cor)
    return img


def granular(img, forca=10, semente=1):
    rnd = random.Random(semente)
    ruido = Image.effect_noise(img.size, 40).convert("L")
    ruido = ruido.point(lambda v: 128 + (v - 128) * forca // 40)
    return Image.blend(img, Image.merge("RGB", (ruido,) * 3), 0.06)


def vinheta(img):
    m = Image.new("L", img.size, 0)
    ImageDraw.Draw(m).ellipse([-L * 0.1, -A * 0.25, L * 1.1, A * 1.25], fill=255)
    m = m.filter(ImageFilter.GaussianBlur(160))
    escuro = Image.new("RGB", img.size, (8, 8, 12))
    return Image.composite(img, escuro, m)


def luz_de_cinema():  # pôr do sol sobre o mar
    img = degrade((255, 150, 80), (120, 40, 110))
    d = ImageDraw.Draw(img)
    cx, cy, r = L // 2, 380, 190
    for y in range(cy - r, cy + 1):
        meia = int(math.sqrt(max(r * r - (y - cy) ** 2, 0)))
        t = (y - (cy - r)) / r
        cor = (255, int(225 - 90 * t), int(120 - 60 * t))
        if y > cy - 70 and (y // 14) % 2 == 0:
            continue
        d.line([(cx - meia, y), (cx + meia, y)], fill=cor)
    mar = degrade((60, 30, 90), (20, 12, 40), (L, A - cy))
    img.paste(mar, (0, cy))
    for i in range(14):
        y = cy + 12 + i * i * 1.7
        larg = 40 + i * 22
        d.line([(cx - larg, y), (cx + larg, y)], fill=(255, 170, 110), width=2)
    return img


def estrada_velha():  # estrada de terra em perspectiva
    img = degrade((236, 196, 140), (150, 96, 60))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, L, 300], fill=(244, 222, 180))
    d.ellipse([L // 2 + 60, 120, L // 2 + 160, 220], fill=(250, 240, 220))
    d.polygon([(L // 2 - 20, 300), (L // 2 + 20, 300), (L // 2 + 330, A), (L // 2 - 330, A)], fill=(110, 70, 45))
    for i in range(8):
        y0 = 310 + i * i * 7
        y1 = y0 + 6 + i * 3
        d.polygon([(L // 2 - 2 - i, y0), (L // 2 + 2 + i, y0), (L // 2 + 3 + i, y1), (L // 2 - 3 - i, y1)], fill=(240, 210, 160))
    d.line([(0, 300), (L, 300)], fill=(120, 80, 50), width=3)
    return img


def varanda():  # verde claro, sol e folhas
    img = degrade((200, 228, 196), (120, 170, 140))
    d = ImageDraw.Draw(img)
    d.ellipse([L // 2 - 170, 110, L // 2 + 170, 450], fill=(250, 214, 120))
    rnd = random.Random(3)
    for i in range(9):
        x = L // 2 - 300 + i * 75
        a = rnd.uniform(-0.6, 0.6)
        for k in range(30):
            t = k / 29
            px = x + math.sin(a) * 260 * t
            py = A + 20 - 330 * t
            w = 26 * math.sin(math.pi * t)
            d.ellipse([px - w, py - 10, px + w, py + 10], fill=(46, 110, 80) if i % 2 else (64, 136, 96))
    return img


def ceu_de_agosto():  # azul profundo, lua e estrelas
    img = degrade((22, 34, 82), (8, 10, 30))
    d = ImageDraw.Draw(img)
    rnd = random.Random(8)
    for _ in range(260):
        x, y = rnd.randint(L // 2 - 360, L // 2 + 360), rnd.randint(0, A)
        s = rnd.choice([1, 1, 1, 2, 2, 3])
        d.ellipse([x, y, x + s, y + s], fill=(230, 236, 255))
    d.ellipse([L // 2 - 150, 170, L // 2 + 150, 470], fill=(240, 236, 214))
    d.ellipse([L // 2 - 90, 140, L // 2 + 200, 450], fill=(16, 22, 58))
    return img


def cafe_frio():  # xícara vista de cima
    img = degrade((236, 222, 200), (210, 186, 156))
    d = ImageDraw.Draw(img)
    cx, cy = L // 2, A // 2
    d.ellipse([cx + 150, cy - 40, cx + 260, cy + 50], outline=(120, 80, 60), width=26)
    for r, cor in ((230, (250, 246, 238)), (190, (232, 224, 210)), (165, (96, 58, 38)), (120, (120, 76, 50)), (60, (150, 100, 70))):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=cor)
    return img


def ultimo_bonde():  # linhas diagonais em verde-azulado
    img = degrade((40, 92, 96), (16, 34, 40))
    d = ImageDraw.Draw(img)
    for i in range(-20, 40):
        x = L // 2 - 360 + i * 36
        d.line([(x, A), (x + 420, 0)], fill=(70, 140, 140) if i % 3 else (240, 200, 90), width=4 if i % 3 else 7)
    d.rectangle([L // 2 - 360, 470, L // 2 + 360, 540], fill=(16, 34, 40))
    d.rectangle([L // 2 - 200, 486, L // 2 + 200, 524], fill=(240, 200, 90))
    return img


artes = [luz_de_cinema, estrada_velha, varanda, ceu_de_agosto, cafe_frio, ultimo_bonde]
for i, arte in enumerate(artes, 1):
    img = vinheta(granular(arte(), semente=i))
    img.save(saida / f"capa{i}.jpg", quality=92)
    img.crop(((L - A) // 2, 0, (L + A) // 2, A)).resize((240, 240)).save(saida / f"amostra{i}.png")

amostras = [Image.open(saida / f"amostra{i}.png") for i in range(1, len(artes) + 1)]
grade = Image.new("RGB", (240 * 3 + 20, 240 * 2 + 10), (40, 40, 40))
for i, a in enumerate(amostras):
    grade.paste(a, ((i % 3) * 250, (i // 3) * 250))
grade.save(saida / "amostras.png")
