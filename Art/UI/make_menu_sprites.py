# Ana menünün ikon, gölge, perde, buton ve panel sprite'larını üretir. İkon ve paneller beyaz (Unity'de renklendirilir),
# butonlar renkli: düz yüz, sert alt kalınlık, kesik (pah kırılmış) köşeler. Çalıştırma: python Art/UI/make_menu_sprites.py
import math
import os
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "UI", "Menu")
SS = 4  # Kenar yumuşatma için büyük çizip küçültür.


def canvas(size):
    return Image.new("RGBA", (size * SS, size * SS), (255, 255, 255, 0))


def save(img, name, size):
    img = img.resize((size, size), Image.LANCZOS)
    img.save(os.path.join(OUT, name))


def shadow(name, size, radius, blur):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    pad = blur * 2
    ImageDraw.Draw(img).rounded_rectangle((pad, pad, size - 1 - pad, size - 1 - pad), radius, fill=(0, 0, 0, 255))
    img = img.filter(ImageFilter.GaussianBlur(blur))
    white = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    white.putalpha(img.getchannel("A"))
    white.save(os.path.join(OUT, name))


def hex_rgba(h, a=255):
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


def chamfer(x0, y0, x1, y1, c):
    # Köşeleri kesik dikdörtgen (low-poly poligon dili).
    return [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c), (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]


def chamfer_sprite(name, w, h, c, layers):
    # layers: (y0, y1, renk) üstten alta; aynı genişlikte üst üste kesik köşeli katmanlar.
    img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for y0, y1, color in layers:
        d.polygon(chamfer(0, y0 * SS, w * SS - 1, y1 * SS - 1, c * SS), fill=color)
    img.resize((w, h), Image.LANCZOS).save(os.path.join(OUT, name))


# Butonlar: 192x72, yüz 64 px + 8 px alt kalınlık. Basılı hali: yüz 70 px + 2 px kalınlık (Unity'de kısalır).
BUTTONS = {"Coral": ("F2542D", "B83A1B"), "Ocean": ("1F5F8B", "154566")}
BUTTON_W, BUTTON_H, LIP, CUT = 192, 72, 8, 12


def button(style):
    face, lip = BUTTONS[style]
    chamfer_sprite(f"Button{style}.png", BUTTON_W, BUTTON_H, CUT,
                   [(LIP, BUTTON_H, hex_rgba(lip)), (0, BUTTON_H - LIP, hex_rgba(face))])
    chamfer_sprite(f"Button{style}Pressed.png", BUTTON_W, BUTTON_H, CUT,
                   [(2, BUTTON_H, hex_rgba(lip)), (0, BUTTON_H - 2, hex_rgba(face))])
    # Kare (ayar okları): 64x72.
    chamfer_sprite(f"Square{style}.png", 64, BUTTON_H, CUT,
                   [(LIP, BUTTON_H, hex_rgba(lip)), (0, BUTTON_H - LIP, hex_rgba(face))])
    chamfer_sprite(f"Square{style}Pressed.png", 64, BUTTON_H, CUT,
                   [(2, BUTTON_H, hex_rgba(lip)), (0, BUTTON_H - 2, hex_rgba(face))])


def ghost_button():
    # Hayalet buton: yarı saydam beyaz dolgu, 3 px beyaz çerçeve, kalınlık yok.
    for name, fill in (("ButtonGhost.png", 40), ("ButtonGhostPressed.png", 90)):
        chamfer_sprite(name, BUTTON_W, BUTTON_H, CUT,
                       [(0, BUTTON_H, (255, 255, 255, 235)), (3, BUTTON_H - 3, (255, 255, 255, 0))])
        img = Image.open(os.path.join(OUT, name))
        inner = Image.new("RGBA", (BUTTON_W * SS, BUTTON_H * SS), (0, 0, 0, 0))
        ImageDraw.Draw(inner).polygon(chamfer(3 * SS, 3 * SS, (BUTTON_W - 3) * SS - 1, (BUTTON_H - 3) * SS - 1, (CUT - 1) * SS), fill=(255, 255, 255, fill))
        inner = inner.resize((BUTTON_W, BUTTON_H), Image.LANCZOS)
        ring = Image.new("RGBA", (BUTTON_W * SS, BUTTON_H * SS), (0, 0, 0, 0))
        ImageDraw.Draw(ring).polygon(chamfer(0, 0, BUTTON_W * SS - 1, BUTTON_H * SS - 1, CUT * SS), fill=(255, 255, 255, 235))
        ImageDraw.Draw(ring).polygon(chamfer(3 * SS, 3 * SS, (BUTTON_W - 3) * SS - 1, (BUTTON_H - 3) * SS - 1, (CUT - 1) * SS), fill=(0, 0, 0, 0))
        ring = ring.resize((BUTTON_W, BUTTON_H), Image.LANCZOS)
        Image.alpha_composite(inner, ring).save(os.path.join(OUT, name))


def handle():
    # Ses kaydırıcısının tutamacı: küçük mercan buton.
    face, lip = BUTTONS["Coral"]
    chamfer_sprite("Handle.png", 44, 52, 9, [(6, 52, hex_rgba(lip)), (0, 46, hex_rgba(face))])


def scrim(name, w, h):
    # Buton sütununun arkasındaki yumuşak koyuluk: ortada dolu, kenarlara doğru eriyen elips.
    img = Image.new("RGBA", (w, h))
    px = img.load()
    for y in range(h):
        for x in range(w):
            d = math.hypot((x - (w - 1) / 2) / (w / 2), (y - (h - 1) / 2) / (h / 2))
            a = max(0.0, min(1.0, 1.0 - d)) ** 1.5
            px[x, y] = (255, 255, 255, int(a * 255))
    img.save(os.path.join(OUT, name))


def hand(name, closed, size=48):
    # Köylüyü tutma imleci: krem el, lacivert kontur. Açık: parmaklar uzun ve aralıklı; kapalı: yumruk.
    S = size * SS
    mask = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(mask)
    u = S / 48
    d.rounded_rectangle((12 * u, 20 * u, 38 * u, 44 * u), 7 * u, fill=255)  # avuç
    fingers = [(12, 5), (19, 2), (26, 3), (33, 7)] if not closed else [(12, 15), (19, 13), (26, 13), (33, 15)]
    for x, top in fingers:
        d.rounded_rectangle((x * u, top * u, (x + 6) * u, 28 * u), 3 * u, fill=255)
    if closed:
        d.rounded_rectangle((6 * u, 24 * u, 17 * u, 32 * u), 4 * u, fill=255)  # içe kıvrık başparmak
    else:
        d.polygon([(13 * u, 30 * u), (4 * u, 22 * u), (7 * u, 18 * u), (16 * u, 25 * u)], fill=255)  # açık başparmak
        d.ellipse((3 * u, 17 * u, 9 * u, 23 * u), fill=255)
    outline = mask.filter(ImageFilter.MaxFilter(int(5 * u) | 1))
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(Image.new("RGBA", (S, S), hex_rgba("0B2A3D")), (0, 0), outline)
    img.paste(Image.new("RGBA", (S, S), hex_rgba("FFF4DC")), (0, 0), mask)
    # Parmak araları (kapalı elde boğum çizgileri).
    lines = ImageDraw.Draw(img)
    for x, top in fingers[1:]:
        lines.line((x * u - 0.5 * u, (top + 4) * u, x * u - 0.5 * u, 27 * u), fill=hex_rgba("0B2A3D"), width=int(1.5 * u))
    img = img.resize((size, size), Image.LANCZOS)
    os.makedirs(CURSOR_OUT, exist_ok=True)
    img.save(os.path.join(CURSOR_OUT, name))


CURSOR_OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "UI", "Cursor")


def circle(name, size):
    img = canvas(size)
    ImageDraw.Draw(img).ellipse((0, 0, size * SS - 1, size * SS - 1), fill="white")
    save(img, name, size)


def vignette(name, size):
    img = Image.new("RGBA", (size, size))
    px = img.load()
    c = (size - 1) / 2
    for y in range(size):
        for x in range(size):
            d = math.hypot((x - c) / c, (y - c) / c) / math.sqrt(2)
            a = max(0.0, min(1.0, (d - 0.25) / 0.75)) ** 1.6
            px[x, y] = (255, 255, 255, int(a * 255))
    img.save(os.path.join(OUT, name))


def icon_play(size=128):
    img = canvas(size)
    s = size * SS
    ImageDraw.Draw(img).polygon([(s * 0.28, s * 0.18), (s * 0.28, s * 0.82), (s * 0.84, s * 0.5)], fill="white")
    save(img, "IconPlay.png", size)


def icon_gear(size=128):
    img = canvas(size)
    s = size * SS
    d = ImageDraw.Draw(img)
    c = s / 2
    teeth, r_out, r_in = 8, s * 0.46, s * 0.34
    pts = []
    for i in range(teeth * 4):
        a = i / (teeth * 4) * math.tau
        r = r_out if i % 4 in (1, 2) else r_in
        pts.append((c + math.cos(a) * r, c + math.sin(a) * r))
    d.polygon(pts, fill="white")
    d.ellipse((c - s * 0.36, c - s * 0.36, c + s * 0.36, c + s * 0.36), fill="white")
    d.ellipse((c - s * 0.15, c - s * 0.15, c + s * 0.15, c + s * 0.15), fill=(255, 255, 255, 0))
    save(img, "IconGear.png", size)


def icon_trophy(size=128):
    img = canvas(size)
    s = size * SS
    d = ImageDraw.Draw(img)
    w = s * 0.06
    d.pieslice((s * 0.24, -s * 0.18, s * 0.76, s * 0.62), 0, 180, fill="white")  # Kupa
    d.rectangle((s * 0.24, s * 0.1, s * 0.76, s * 0.22), fill="white")
    d.arc((s * 0.06, s * 0.14, s * 0.36, s * 0.46), 90, 270, fill="white", width=int(w))  # Kulplar
    d.arc((s * 0.64, s * 0.14, s * 0.94, s * 0.46), 270, 90, fill="white", width=int(w))
    d.rectangle((s * 0.45, s * 0.58, s * 0.55, s * 0.76), fill="white")  # Sap
    d.rounded_rectangle((s * 0.28, s * 0.76, s * 0.72, s * 0.9), s * 0.03, fill="white")  # Kaide
    save(img, "IconTrophy.png", size)


def icon_exit(size=128):
    img = canvas(size)
    s = size * SS
    d = ImageDraw.Draw(img)
    w = int(s * 0.09)
    d.line([(s * 0.5, s * 0.14), (s * 0.18, s * 0.14), (s * 0.18, s * 0.86), (s * 0.5, s * 0.86)], fill="white", width=w, joint="curve")
    d.line([(s * 0.42, s * 0.5), (s * 0.82, s * 0.5)], fill="white", width=w)
    d.polygon([(s * 0.92, s * 0.5), (s * 0.7, s * 0.3), (s * 0.7, s * 0.7)], fill="white")
    save(img, "IconExit.png", size)


def waves(size=256):
    # Logonun altındaki dalga çizgileri.
    img = canvas(size)
    s = size * SS
    d = ImageDraw.Draw(img)
    for row in range(3):
        pts = []
        for i in range(801):
            t = i / 800
            pts.append((s * (0.06 + 0.88 * t), s * (0.3 + row * 0.2 + 0.05 * math.sin(t * math.tau * 2))))
        r = s * 0.035
        for x, y in pts:  # Yuvarlak uçlu kalın çizgi.
            d.ellipse((x - r, y - r, x + r, y + r), fill="white")
    save(img, "Waves.png", size)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    shadow("Shadow.png", 96, 22, 10)
    circle("Circle.png", 64)
    vignette("Vignette.png", 256)
    icon_play()
    icon_gear()
    icon_trophy()
    icon_exit()
    waves()
    for style in BUTTONS:
        button(style)
    ghost_button()
    handle()
    chamfer_sprite("Panel.png", 96, 96, 16, [(0, 96, (255, 255, 255, 255))])
    chamfer_sprite("Bar.png", 64, 20, 5, [(0, 20, (255, 255, 255, 255))])
    scrim("Scrim.png", 128, 128)
    hand("HandOpen.png", False)
    hand("HandClosed.png", True)
