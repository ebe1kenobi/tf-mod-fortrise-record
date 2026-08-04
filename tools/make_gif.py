#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Assemble les PNG des enregistrements du mod record en GIF animes, UN PAR ROUND.

Par defaut le script traite tous les dossiers match_* situes dans SON PROPRE
repertoire : on le depose dans Saves/<mod>/Recordings/ et on le lance sans
argument.

Tous les GIF sont regroupes dans un sous-dossier gif/ place A COTE des dossiers de
match (et non parmi les PNG), nommes <dossier_du_match>_round_<numero>.gif :

    Recordings/
      gif/
        match_20260804_193244_round_00.gif
        match_20260804_193244_round_01.gif
      match_20260804_193244/
        round_00_frame_000000.png
        ...

Usage :
    python make_gif.py                      # tous les match_* du dossier du script
    python make_gif.py -d chemin/Recordings # autre repertoire de matchs
    python make_gif.py --quality low        # fichiers nettement plus legers
    python make_gif.py --colors 32          # reglage fin de la palette
    python make_gif.py --fps 30 --scale 2   # vitesse et agrandissement
    python make_gif.py --force              # refait les GIF deja presents

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


# Tous les GIF produits atterrissent ici, a cote des dossiers de match.
GIF_DIRNAME = "gif"

# round_00_frame_000123.png -> (0, 123)
FRAME_RE = re.compile(r"^round_(\d+)_frame_(\d+)\.png$", re.IGNORECASE)
# Ancien format, avant l'ajout du numero de round.
LEGACY_RE = re.compile(r"^frame_(\d+)\.png$", re.IGNORECASE)

# Deux leviers sur la taille, mesures sur un round reel de 277 images (7,2 Mo au
# depart) : la palette pese peu (256 -> 48 couleurs ne fait que -18 %), le nombre
# d'images pese enormement (1 sur 2 = -42 %). D'ou ces profils, qui jouent
# surtout sur la cadence.
#   (couleurs, 1 image conservee sur N)
QUALITY_PROFILES = {
    "high":   (256, 1),   # ~4,1 Mo  - toutes les images
    "medium": (128, 2),   # ~2,2 Mo  - fluide, deux fois plus leger
    "low":    (48, 3),    # ~1,2 Mo  - saccade mais tres leger
}


def group_rounds(folder: Path) -> dict[str, list[Path]]:
    """PNG du dossier groupes par round, chaque liste triee par numero de frame.

    Repli sur l'ancien nommage frame_*.png (un seul groupe) pour rester capable
    de traiter les enregistrements anterieurs.
    """
    rounds: dict[str, list[tuple[int, Path]]] = {}
    legacy: list[tuple[int, Path]] = []

    for path in folder.iterdir():
        if not path.is_file():
            continue
        m = FRAME_RE.match(path.name)
        if m:
            rounds.setdefault(m.group(1), []).append((int(m.group(2)), path))
            continue
        m = LEGACY_RE.match(path.name)
        if m:
            legacy.append((int(m.group(1)), path))

    if not rounds and legacy:
        rounds["match"] = legacy

    return {key: [p for _, p in sorted(items)] for key, items in sorted(rounds.items())}


def build_palette(images: list[Image.Image], colors: int) -> Image.Image:
    """Palette commune a tout le round.

    Quantifier chaque image separement donnerait une palette locale par frame :
    le GIF grossit et les couleurs scintillent d'une image a l'autre. On echantillonne
    donc quelques frames reparties, on les empile en une seule image, et sa palette
    sert de reference a toutes les autres.
    """
    step = max(1, len(images) // 16)
    sample = images[::step][:16]

    width = sample[0].width
    stack = Image.new("RGB", (width, sample[0].height * len(sample)))
    for i, img in enumerate(sample):
        stack.paste(img, (0, i * sample[0].height))

    return stack.quantize(colors=colors, method=Image.MEDIANCUT)


def build_gif(frames: list[Path], output: Path, fps: float, scale: int,
              colors: int, every: int, loop: bool) -> tuple[int, int]:
    """Ecrit un GIF ; rend (nombre d'images, taille du fichier en octets)."""
    # Sous-echantillonnage temporel : on garde une image sur `every` et on allonge
    # d'autant la duree d'affichage, pour que le round garde sa vitesse reelle.
    frames = frames[::every]

    images = []
    base_size = None
    for path in frames:
        img = Image.open(path).convert("RGB")
        if base_size is None:
            base_size = img.size
        elif img.size != base_size:
            # Un GIF exige des images de meme taille : on pave sur la premiere
            # plutot que d'echouer.
            canvas = Image.new("RGB", base_size, (0, 0, 0))
            canvas.paste(img, (0, 0))
            img = canvas
        if scale > 1:
            # NEAREST : pixels nets, indispensable sur du pixel art.
            img = img.resize((img.width * scale, img.height * scale), Image.NEAREST)
        images.append(img)

    palette = build_palette(images, colors)
    # dither=NONE : le tramage crée du bruit qui double la taille d'un GIF et
    # abime le rendu pixel art.
    images = [img.quantize(palette=palette, dither=Image.Dither.NONE) for img in images]

    # GIF : duree par image en ms, granularite 10 ms.
    duration_ms = max(20, round(1000.0 * every / fps))

    images[0].save(
        output,
        save_all=True,
        append_images=images[1:],
        duration=duration_ms,
        loop=(0 if loop else None),
        # disposal=1 (ne pas effacer avant l'image suivante) plutot que 2 : Pillow
        # peut alors ne stocker que les zones qui changent d'une image a l'autre,
        # ce qui fait -43 % sur un round reel. Le rendu est strictement identique
        # (verifie image par image) : nos frames sont opaques et couvrent tout
        # l'ecran, il n'y a donc aucune trainee possible.
        disposal=1,
        optimize=True,
    )
    return len(images), output.stat().st_size


def process_match(match_dir: Path, gif_dir: Path, args, colors: int, every: int) -> int:
    """Traite un dossier match_* ; rend le nombre de GIF ecrits.

    Les GIF ne sont pas deposes parmi les PNG mais regroupes dans gif_dir, et
    prefixes du nom du match pour rester identifiables une fois rassembles.
    """
    rounds = group_rounds(match_dir)
    if not rounds:
        return 0

    written = 0
    for key, frames in rounds.items():
        # match_20260804_193244_round_00.gif ; sans round pour l'ancien nommage.
        name = (f"{match_dir.name}.gif" if key == "match"
                else f"{match_dir.name}_round_{key}.gif")
        output = gif_dir / name

        if output.exists() and not args.force:
            print(f"  {name:44} saute (existe deja, --force pour refaire)")
            continue

        gif_dir.mkdir(parents=True, exist_ok=True)
        count, size = build_gif(frames, output, args.fps, args.scale, colors,
                                every, not args.no_loop)
        print(f"  {name:44} {count:4} images  {size / 1024:8.0f} Ko")
        written += 1

    return written


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Cree un GIF anime par round pour chaque dossier match_*.")
    parser.add_argument("-d", "--dir", default=None,
                        help="repertoire contenant les dossiers match_* "
                             "(defaut : le repertoire du script)")
    parser.add_argument("--fps", type=float, default=15.0,
                        help="images par seconde du GIF (defaut : 15)")
    parser.add_argument("--scale", type=int, default=1,
                        help="facteur d'agrandissement entier, pixels nets (defaut : 1)")
    parser.add_argument("--quality", choices=["high", "medium", "low"], default="high",
                        help="compromis taille/fidelite : high (toutes les images), "
                             "medium (1 sur 2, ~2x plus leger), low (1 sur 3, ~3.5x "
                             "plus leger) (defaut : high)")
    parser.add_argument("--colors", type=int, default=None,
                        help="nombre de couleurs exact (2-256) ; prioritaire sur --quality")
    parser.add_argument("--every", type=int, default=None,
                        help="ne garder qu'une image sur N (la vitesse de lecture est "
                             "conservee) ; prioritaire sur --quality")
    parser.add_argument("--no-loop", action="store_true",
                        help="ne pas boucler (par defaut le GIF boucle a l'infini)")
    parser.add_argument("--force", action="store_true",
                        help="recalculer les GIF deja presents")
    args = parser.parse_args()

    if args.fps <= 0:
        sys.exit("--fps doit etre > 0")
    if args.scale < 1:
        sys.exit("--scale doit etre >= 1")

    profile_colors, profile_every = QUALITY_PROFILES[args.quality]
    colors = args.colors if args.colors is not None else profile_colors
    every = args.every if args.every is not None else profile_every
    if not 2 <= colors <= 256:
        sys.exit("--colors doit etre compris entre 2 et 256")
    if every < 1:
        sys.exit("--every doit etre >= 1")

    root = Path(args.dir) if args.dir else Path(__file__).resolve().parent
    if not root.is_dir():
        sys.exit(f"Repertoire introuvable : {root}")

    # Tous les GIF sont regroupes ici, a cote des dossiers de match et non dedans.
    gif_dir = root / GIF_DIRNAME

    matches = sorted(d for d in root.iterdir()
                     if d.is_dir() and d.name.startswith("match_"))
    # Tolerance : si on pointe directement sur un dossier de match, on le traite.
    if not matches and group_rounds(root):
        matches = [root]
        # Dans ce cas le dossier pointe EST le match : on sort les GIF a cote de
        # lui plutot que de les enfouir dedans.
        gif_dir = root.parent / GIF_DIRNAME

    if not matches:
        sys.exit(f"Aucun dossier match_* dans {root}")

    cadence = "toutes les images" if every == 1 else f"1 image sur {every}"
    print(f"{len(matches)} match(s) dans {root}  ({colors} couleurs, {cadence})")
    print(f"sortie : {gif_dir}")
    total = 0
    for match_dir in matches:
        print(f"{match_dir.name} :")
        written = process_match(match_dir, gif_dir, args, colors, every)
        if written == 0:
            print("  (rien a faire)")
        total += written

    print(f"{total} GIF ecrit(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
