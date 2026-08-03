#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Assemble les PNG d'un dossier d'enregistrement (frame_000000.png, ...) en un GIF anime.

Usage :
    python make_gif.py                 # dossier courant -> match.gif a 15 fps
    python make_gif.py -o sortie.gif   # nom de sortie
    python make_gif.py --fps 30        # vitesse de lecture
    python make_gif.py --scale 2       # agrandit x2 (pixels nets)
    python make_gif.py -d chemin/match_20260101_120000

Dependance : Pillow  ->  pip install Pillow
"""

import argparse
import re
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow est requis : pip install Pillow")


def frame_number(path: Path) -> int:
    """Extrait le numero de frame pour un tri numerique (frame_000042.png -> 42)."""
    m = re.search(r"(\d+)", path.stem)
    return int(m.group(1)) if m else -1


def collect_frames(folder: Path) -> list[Path]:
    """PNG 'frame_*.png' du dossier, tries par numero ; repli sur tous les *.png."""
    frames = sorted(folder.glob("frame_*.png"), key=frame_number)
    if not frames:
        frames = sorted(folder.glob("*.png"), key=frame_number)
    return frames


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Cree un GIF anime a partir d'une sequence de PNG.")
    parser.add_argument("-d", "--dir", default=".",
                        help="dossier contenant les PNG (defaut : dossier courant)")
    parser.add_argument("-o", "--output", default="match.gif",
                        help="fichier GIF de sortie (defaut : match.gif)")
    parser.add_argument("--fps", type=float, default=15.0,
                        help="images par seconde du GIF (defaut : 15)")
    parser.add_argument("--scale", type=int, default=1,
                        help="facteur d'agrandissement entier, pixels nets (defaut : 1)")
    parser.add_argument("--no-loop", action="store_true",
                        help="ne pas boucler (par defaut le GIF boucle a l'infini)")
    args = parser.parse_args()

    folder = Path(args.dir)
    if not folder.is_dir():
        sys.exit(f"Dossier introuvable : {folder}")

    frames = collect_frames(folder)
    if not frames:
        sys.exit(f"Aucun PNG trouve dans {folder.resolve()}")

    if args.fps <= 0:
        sys.exit("--fps doit etre > 0")

    # GIF : duree par image en ms (granularite 10 ms, arrondie au mieux).
    duration_ms = max(20, round(1000.0 / args.fps))

    images = []
    base_size = None
    for path in frames:
        img = Image.open(path).convert("RGB")
        if base_size is None:
            base_size = img.size
        elif img.size != base_size:
            # Un GIF doit avoir des images de taille identique : on recadre/pave
            # sur la taille de la premiere image plutot que d'echouer.
            canvas = Image.new("RGB", base_size, (0, 0, 0))
            canvas.paste(img, (0, 0))
            img = canvas
        if args.scale > 1:
            img = img.resize(
                (img.width * args.scale, img.height * args.scale),
                Image.NEAREST)  # NEAREST = pixels nets (pixel art)
        images.append(img)

    output = Path(args.output)
    images[0].save(
        output,
        save_all=True,
        append_images=images[1:],
        duration=duration_ms,
        loop=(None if args.no_loop else 0),
        disposal=2,       # efface chaque frame avant la suivante (pas de trainee)
        optimize=True,
    )

    print(f"{len(images)} images -> {output.resolve()} "
          f"({args.fps:g} fps, {duration_ms} ms/image"
          f"{', x' + str(args.scale) if args.scale > 1 else ''})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
