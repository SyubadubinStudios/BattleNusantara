"""Composes the social media images in docs/marketing/ from the key art in assets/marketing/, the hero portraits and
real screenshots in docs/screenshots/. Run: python scripts/marketing.py   (needs Pillow and the Windows fonts
Palatino Linotype and Segoe UI).

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "assets/marketing"
SHOTS = ROOT / "docs/screenshots"
PORTRAITS = ROOT / "assets/concepts"
OUT = ROOT / "docs/marketing"
OUT.mkdir(parents=True, exist_ok=True)
FONTS = Path("C:/Windows/Fonts")
GOLD, TEAL, INK, CREAM = (232, 205, 140), (131, 201, 186), (10, 22, 30), (241, 230, 205)
CREDIT = "Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios"


def font(name, size):
    return ImageFont.truetype(str(FONTS / name), size)


def cover(image, size):
    """Scales and centre-crops to fill size."""
    w, h = size
    scale = max(w / image.width, h / image.height)
    image = image.resize((round(image.width * scale), round(image.height * scale)), Image.LANCZOS)
    left, top = (image.width - w) // 2, (image.height - h) // 2
    return image.crop((left, top, left + w, top + h))


def shade(image, top=0.0, bottom=0.0):
    """Darkens the top and bottom for legible text."""
    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    h = image.height
    for y in range(h):
        alpha = 0
        if top and y < h * top:
            alpha = max(alpha, int(210 * (1 - y / (h * top))))
        if bottom and y > h * (1 - bottom):
            alpha = max(alpha, int(230 * ((y - h * (1 - bottom)) / (h * bottom))))
        if alpha:
            draw.line([(0, y), (image.width, y)], fill=(*INK, alpha))
    return Image.alpha_composite(image.convert("RGBA"), overlay)


def text(draw, xy, value, size, fill=CREAM, bold=False, serif=False, anchor="la", spacing=1.0, shadow=True):
    face = font(("palab.ttf" if bold else "pala.ttf") if serif else ("segoeuib.ttf" if bold else "segoeui.ttf"), size)
    if spacing != 1.0:
        x, y = xy
        if anchor[0] == "m":
            x -= sum(face.getlength(c) * spacing for c in value) / 2
            anchor = "l" + anchor[1]
        for c in value:
            if shadow:
                draw.text((x + 2, y + 2), c, font=face, fill=(0, 0, 0, 160), anchor=anchor)
            draw.text((x, y), c, font=face, fill=fill, anchor=anchor)
            x += face.getlength(c) * spacing
        return
    if shadow:
        draw.text((xy[0] + 2, xy[1] + 2), value, font=face, fill=(0, 0, 0, 170), anchor=anchor)
    draw.text(xy, value, font=face, fill=fill, anchor=anchor)


def logo(draw, center_x, y, scale=1.0, anchor="m"):
    text(draw, (center_x, y), "BATTLE OF", int(26 * scale), GOLD, bold=True, anchor=anchor + "m", spacing=1.6)
    text(draw, (center_x, y + 62 * scale), "NUSANTARA", int(86 * scale), CREAM, serif=True, anchor=anchor + "m", spacing=1.12)


def footer(image, note):
    draw = ImageDraw.Draw(image)
    w, h = image.size
    text(draw, (w // 2, h - 58), CREDIT, 22, CREAM, anchor="mm")
    text(draw, (w // 2, h - 28), note, 17, (190, 205, 205), anchor="mm")


def badge(draw, xy, value, size=24):
    face = font("segoeuib.ttf", size)
    x, y = xy
    width = face.getlength(value) + 36
    draw.rounded_rectangle([x, y, x + width, y + size + 22], radius=(size + 22) // 2, outline=GOLD, width=2, fill=(10, 22, 30, 170))
    draw.text((x + 18, y + 9), value, font=face, fill=GOLD)
    return x + width + 14


def save(image, name):
    image.convert("RGB").save(OUT / name, quality=90, optimize=True)
    print(name, image.size)


KEY_NOTE = "Ilustrasi key art, bukan cuplikan permainan / Key art illustration, not gameplay"
SHOT_NOTE = "Tangkapan layar asli dari game / Real in-game screenshots"

# 1. Announcement, landscape for X, Facebook and YouTube community posts.
art = Image.open(ART / "keyart-heroes.jpg")
img = shade(cover(art, (1600, 900)), top=0.34, bottom=0.3)
d = ImageDraw.Draw(img)
logo(d, 800, 70)
text(d, (800, 222), "Legenda Nusantara bertarung lintas zaman", 34, CREAM, serif=True, anchor="mm")
x = 800 - 330
for label in ("RTS", "MOBA", "ADVENTURE", "CO-OP LAN"):
    x = badge(d, (x, 742), label)
footer(img, KEY_NOTE)
save(img, "01-pengumuman-1600x900.jpg")

# 2. The same key art, square for Instagram and Facebook feeds.
img = shade(cover(art, (1080, 1080)), top=0.3, bottom=0.26)
d = ImageDraw.Draw(img)
logo(d, 540, 72, 0.9)
text(d, (540, 214), "Enam penjaga. Satu dunia yang harus diselamatkan.", 30, CREAM, serif=True, anchor="mm")
footer(img, KEY_NOTE)
save(img, "02-enam-penjaga-1080x1080.jpg")

# 3. Superhero faction, vertical for Stories, Reels and TikTok covers.
art = Image.open(ART / "keyart-superheroes.jpg")
img = shade(cover(art, (1080, 1920)), top=0.16, bottom=0.36)
d = ImageDraw.Draw(img)
logo(d, 540, 96, 0.95)
text(d, (540, 1300), "FAKSI PAHLAWAN SUPER", 30, GOLD, bold=True, anchor="mm", spacing=1.3)
text(d, (540, 1380), "Satria Bledhek", 76, CREAM, serif=True, anchor="mm")
text(d, (540, 1450), "Petir yang melompat dari musuh ke musuh", 30, (200, 228, 255), anchor="mm")
text(d, (540, 1560), "Rara Angkasa", 76, CREAM, serif=True, anchor="mm")
text(d, (540, 1630), "Terbang menerjang, gelombang kejut raksasa", 30, (220, 205, 255), anchor="mm")
text(d, (540, 1720), "Hero orisinal Nusantara", 26, GOLD, anchor="mm")
footer(img, KEY_NOTE)
save(img, "03-pahlawan-super-story-1080x1920.jpg")

# 4. Adventure, square.
art = Image.open(ART / "keyart-adventure.jpg")
img = shade(cover(art, (1080, 1080)), top=0.3, bottom=0.3)
d = ImageDraw.Draw(img)
logo(d, 540, 72, 0.9)
text(d, (540, 214), "Gua Mistis menanti.", 40, CREAM, serif=True, anchor="mm")
text(d, (540, 862), "Misi bersuara · cutscene · tugas sampingan yang selalu berbeda", 26, CREAM, anchor="mm")
text(d, (540, 902), "Tanya Juru Kunci dengan kalimatmu sendiri", 26, TEAL, anchor="mm")
footer(img, KEY_NOTE)
save(img, "04-adventure-1080x1080.jpg")

# 5. The six heroes, square grid of portraits.
img = Image.new("RGBA", (1080, 1080), (*INK, 255))
d = ImageDraw.Draw(img)
logo(d, 540, 62, 0.8)
heroes = [("gajah-mada", "Gajah Mada"), ("cut-nyak-dien", "Cut Nyak Dien"), ("nyi-roro-kidul", "Nyi Roro Kidul"),
          ("republik", "Penjaga Republik"), ("bledhek", "Satria Bledhek"), ("rara-angkasa", "Rara Angkasa")]
cell_w, cell_h, gap = 320, 400, 20
x0, y0 = (1080 - 3 * cell_w - 2 * gap) // 2, 160
for i, (key, name) in enumerate(heroes):
    x, y = x0 + (i % 3) * (cell_w + gap), y0 + (i // 3) * (cell_h + gap)
    portrait = cover(Image.open(PORTRAITS / f"{key}.jpg"), (cell_w, cell_h)).convert("RGBA")
    portrait = shade(portrait, bottom=0.35)
    img.paste(portrait, (x, y))
    d.rectangle([x, y, x + cell_w - 1, y + cell_h - 1], outline=(*GOLD, 160), width=2)
    text(d, (x + cell_w // 2, y + cell_h - 34), name, 30, CREAM, serif=True, anchor="mm")
text(d, (540, 1000), "Pilih legendamu.", 34, GOLD, serif=True, anchor="mm")
footer(img, "Potret hero dari game / Hero portraits used in the game")
save(img, "05-pilih-legenda-1080x1080.jpg")

# 6. Real gameplay, 4:5 for feeds: four screenshots with captions.
shots = [("cutscene.png", "Cutscene pembuka Adventure"), ("characters.png", "Pertempuran MOBA, karakter Blender"),
         ("weather-rain.png", "Hujan saat senja: pandangan memendek"), ("captain-talk.png", "Tanya Nakhoda Tua apa saja")]
img = Image.new("RGBA", (1080, 1350), (*INK, 255))
d = ImageDraw.Draw(img)
logo(d, 540, 58, 0.75)
cell_w, cell_h = 520, 500
for i, (name, caption) in enumerate(shots):
    x, y = 13 + (i % 2) * (cell_w + 14), 150 + (i // 2) * (cell_h + 60)
    shot = Image.open(SHOTS / name).convert("RGB")
    # Crop away the page header and footer so the arena fills the frame.
    shot = shot.crop((int(shot.width * .2), int(shot.height * .17), int(shot.width * .8), int(shot.height * .78)))
    img.paste(cover(shot, (cell_w, cell_h)), (x, y))
    d.rectangle([x, y, x + cell_w - 1, y + cell_h - 1], outline=(*GOLD, 140), width=2)
    text(d, (x + cell_w // 2, y + cell_h + 28), caption, 24, CREAM, anchor="mm")
footer(img, SHOT_NOTE)
save(img, "06-cuplikan-permainan-1080x1350.jpg")
