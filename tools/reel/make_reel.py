"""Cuts the real-app clips from the demo recorder into short promo videos with captions.

  python tools/reel/make_reel.py [clips folder] [output folder]

Makes Zoomies-reel-9x16.mp4 (Instagram Reels, Pinterest) and Zoomies-reel-4x5.mp4 (LinkedIn feed).
Needs the clips from:  DemoRecorder record --script inserts --theme midnight --out demo/reel-src
and Pillow. Captions are drawn as images because CapCut's ffmpeg has no text filter.
"""
import os
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CLIPS = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "demo", "reel-src")
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "demo")
FFMPEG = os.path.join(os.environ["LOCALAPPDATA"], "CapCut", "Apps", "6.3.0.2438", "ffmpeg.exe")
LOGO = os.path.join(ROOT, "src", "Zoomies", "Assets", "logo.png")
BOLD = r"C:\Windows\Fonts\segoeuib.ttf"
REGULAR = r"C:\Windows\Fonts\segoeui.ttf"
BG = (11, 11, 15)
WHITE, ACCENT, GREY = (255, 255, 255), (165, 161, 255), (161, 161, 170)

# clip, where to start (seconds from the start, or negative = from the end), length,
# crop x of a 1280-wide window of the 1920x1080 recording, and captions as (from, to, line 1, line 2)
SEGMENTS = [
    ("A-find-intern", 0.6, 5.0, 20, [(0, 1.9, "POV: your Downloads", "has 300 files"),
                                     (1.9, 99, "find any file", "in 2 seconds")]),
    ("C-organize-all", -5.5, 5.5, 625, [(0, 99, "sort the chaos", "in one click")]),
    ("E-duplicates", -4.0, 4.0, 320, [(0, 99, "4 copies of the", "same résumé??")]),
    ("H-undo", -3.5, 3.5, 320, [(0, 99, "changed your mind?", "undo. anytime.")]),
    ("I-no-matches", 0.6, 4.6, 320, [(0, 2.8, "can it find", "my motivation?"),
                                     (2.8, 99, "it finds files,", "bestie.")]),
]
VIDEO_H = 911  # a 1280x1080 crop scaled to 1080 wide


def run(args):
    result = subprocess.run(args, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True, errors="replace")
    if result.returncode != 0:
        raise RuntimeError(result.stderr[-2000:])


def lines_png(path, width, lines, gap=18):
    """Centered lines of text on a transparent image. lines: (text, font file, size, colour)."""
    fonts = [ImageFont.truetype(f, s) for (_, f, s, _) in lines]
    boxes = [font.getbbox(t) for font, (t, *_ ) in zip(fonts, lines)]
    height = sum(b[3] - b[1] for b in boxes) + gap * (len(lines) - 1) + 20
    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    y = 10
    for font, box, (t, _, _, colour) in zip(fonts, boxes, lines):
        draw.text(((width - (box[2] - box[0])) / 2 - box[0], y - box[1]), t, font=font, fill=colour)
        y += box[3] - box[1] + gap
    img.save(path)
    return height


def encode_args(out):
    return ["-c:v", "h264_nvenc", "-preset", "p6", "-cq", "19", "-pix_fmt", "yuv420p", "-r", "30",
            "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", "-y", out]


def make(width, height, name):
    tall = height > 1500
    tmp = tempfile.mkdtemp(prefix="reel-")
    video_y = (height - VIDEO_H) // 2 + (60 if tall else 40)
    cap_size = 76 if tall else 58
    brand = os.path.join(tmp, "brand.png")
    brand_h = lines_png(brand, width, [("Zoomies", BOLD, 44, ACCENT)])
    brand_y = video_y + VIDEO_H + (150 if tall else 50)

    parts = []
    for i, (clip, start, length, crop_x, captions) in enumerate(SEGMENTS):
        src = os.path.join(CLIPS, clip + ".mp4")
        seek = ["-sseof", str(start)] if start < 0 else ["-ss", str(start)]
        inputs = [*seek, "-t", str(length), "-i", src,
                  "-f", "lavfi", "-t", str(length), "-i", "anullsrc=r=44100:cl=stereo",
                  "-loop", "1", "-t", str(length), "-i", brand]
        graph = (f"color=c=0x0B0B0F:s={width}x{height}:r=30:d={length}[bg];"
                 f"[0:v]crop=1280:1080:{crop_x}:0,scale=1080:-2,setsar=1[v];"
                 f"[bg][v]overlay=(W-w)/2:{video_y}[s0];[s0][2:v]overlay=0:{brand_y}[s1]")
        last = "s1"
        for k, (a, b, l1, l2) in enumerate(captions):
            png = os.path.join(tmp, f"cap{i}_{k}.png")
            cap_h = lines_png(png, width, [(l1, BOLD, cap_size, WHITE), (l2, BOLD, cap_size, WHITE)])
            inputs += ["-loop", "1", "-t", str(length), "-i", png]
            cap_y = max(20, video_y - cap_h - (60 if tall else 25))
            graph += f";[{last}][{3 + k}:v]overlay=0:{cap_y}:enable='between(t,{a},{b})'[c{k}]"
            last = f"c{k}"
        part = os.path.join(tmp, f"{i}.mp4")
        run([FFMPEG, "-hide_banner", *inputs, "-filter_complex", graph,
             "-map", f"[{last}]", "-map", "1:a", "-t", str(length), *encode_args(part)])
        parts.append(part)

    # End card: logo, name, what it does, where to get it.
    card = os.path.join(tmp, "card.png")
    img = Image.new("RGB", (width, height), BG)
    logo = Image.open(LOGO).convert("RGBA").resize((240, 240), Image.LANCZOS)
    top = height // 2 - (330 if tall else 270)
    img.paste(logo, ((width - 240) // 2, top), logo)
    text = os.path.join(tmp, "card-text.png")
    lines_png(text, width, [("Zoomies", BOLD, 96, WHITE),
                            ("jump to any file. tidy in one click.", REGULAR, 40, GREY),
                            ("free  ·  open source  ·  Windows", BOLD, 40, ACCENT),
                            ("github.com/lakshmang007/zoomies", REGULAR, 36, GREY)], gap=30)
    words = Image.open(text)
    img.paste(words, (0, top + 280), words)
    img.save(card)
    end = os.path.join(tmp, "end.mp4")
    run([FFMPEG, "-hide_banner", "-loop", "1", "-t", "3.2", "-i", card,
         "-f", "lavfi", "-t", "3.2", "-i", "anullsrc=r=44100:cl=stereo",
         "-filter_complex", "[0:v]fade=t=in:st=0:d=0.3,format=yuv420p[out]",
         "-map", "[out]", "-map", "1:a", "-t", "3.2", *encode_args(end)])
    parts.append(end)

    listing = os.path.join(tmp, "list.txt")
    with open(listing, "w", encoding="utf-8") as f:
        f.writelines(f"file '{p.replace(os.sep, '/')}'\n" for p in parts)
    out = os.path.join(OUT, name)
    run([FFMPEG, "-hide_banner", "-f", "concat", "-safe", "0", "-i", listing, "-c", "copy",
         "-movflags", "+faststart", "-y", out])
    print("wrote", out)


if __name__ == "__main__":
    make(1080, 1920, "Zoomies-reel-9x16.mp4")
    make(1080, 1350, "Zoomies-reel-4x5.mp4")
