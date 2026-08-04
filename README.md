# tf-mod-fortrise-record

Mod FortRise 5 pour TowerFall : enregistre un match complet (images + inputs +
état de jeu), en s'appuyant sur le même buffer de rendu que le système de replay
du jeu (`Engine.Instance.Screen.RenderTarget`).

## Réglages (mod settings)

| Option | Défaut | Rôle |
|--------|--------|------|
| Enable recording | off | Interrupteur maître |
| Captures per second | 15 | FPS de capture (commun images/inputs/état) |
| Record images (PNG) | on | Séquence d'images PNG 320×240 |
| PNG compression | Balanced | Compromis taille/CPU (voir ci-dessous) |
| Record player inputs | on | Inputs de chaque joueur par frame |
| Record game state (positions/grid) | on | Positions joueurs/flèches + grille de niveau |
| Make GIF per round | **off** | Assemble un GIF à la fin de chaque round |
| GIF quality | High | Profil taille/fidélité du GIF |

### PNG compression

**PNG est sans perte : ce réglage ne change jamais l'image**, uniquement la taille
du fichier et le temps CPU du thread d'écriture. Il n'y a pas de « qualité » à
régler comme en JPEG. Mesures sur 27 frames réelles, par image :

| Valeur | Taille | CPU | Remarque |
|--------|--------|-----|----------|
| Fast | ~60 Ko | ~3 ms | |
| **Balanced** (défaut) | **~40 Ko** | **~6 ms** | tient le budget jusqu'à 60 captures/s |
| Smallest | ~36 Ko | ~20 ms | décroche au-delà de ~45 captures/s (frames droppées) |

Le budget du thread d'écriture est de `1/FPS` par frame, soit 66 ms à 15 captures/s
et 16 ms à 60. Au-delà, la file se remplit et les frames excédentaires sont droppées.

**Un match = un dossier**, dans l'espace de sauvegarde du mod :
`<FortRise>/Saves/<nom du mod>/Recordings/match_<horodatage>/`. Il est ouvert au
premier round lancé et fermé au retour au menu / à la carte.

Seul le **jeu actif** est capturé. La capture est gelée hors de cette fenêtre et
reprend automatiquement au round suivant :

| Phase | Capturé | Marqueur |
|-------|---------|----------|
| Cinématique `FIGHT!` (joueurs freeze) | non | `RoundLogic.RoundStarted` (posé par `Session.StartRound`) |
| Round en cours, **mort du dernier joueur comprise** | **oui** | |
| Rewind du replay puis écran de score | non | `Session.EndRound` |
| Pause (Start, hold-to-pause, manette débranchée, perte de focus) | non | `Level.Paused` |
| Chargement entre deux rounds | non | scène ≠ `Level` |

La borne de fin est `Session.EndRound` et non `Level.Ending` : ce dernier est posé
dès que le round est *décidé*, donc avant que la mort du dernier joueur ne soit
jouée à l'écran. `EndRound` arrive au terme du `RoundEndCounter` (90 frames, plus
l'attente des fantômes), soit après la mort, le ralenti et le spotlight — et juste
avant que le `ReplayViewer` ne prenne la main pour le rewind, qu'on ne veut pas
enregistrer.

## Sorties

Les fichiers sont préfixés par le numéro de round (`Session.RoundIndex`, **0 pour
le premier**), pour que le tri alphabétique du dossier suive l'ordre de jeu.

- `round_00_frame_000000.png`, … — images 320×240 (résolution interne du jeu).
- `inputs.jsonl` — une ligne JSON par frame :
  `{"f":<frame>,"r":<round>,"p":[{"i":<player>,"mx":,"my":,"ax":,"ay":,"jump":,"shoot":,"alt":,"dodge":,"arrows":}]}`
  (mx/my = direction, ax/ay = visée, les autres = boutons maintenus 0/1).
- `state.jsonl` — une ligne JSON par frame :
  `{"f":<frame>,"r":<round>,"players":[{"i":,"x":,"y":,"vx":,"vy":,"face":,"aim":,"ground":,"dead":}],"arrows":[{"x":,"y":,"vx":,"vy":,"dir":,"st":,"pi":}]}`
- `round_00_level.json` — grille de solides du niveau (calculée une fois par round) :
  `{"width":32,"height":24,"block":10,"grid":[[0,1,…],…]}` (1 = solide).

Tout est aligné par le champ `"f"`, commun aux images et aux JSONL. `"f"` est
**continu sur tout le match** (il ne repart pas à 0 à chaque round) : chaque frame
capturée a donc un index unique, et `"r"` sert uniquement à retrouver le round.

## GIF depuis le jeu (option `Make GIF per round`)

Désactivé par défaut. Une fois activé, le mod assemble lui-même un GIF à la fin de
chaque round, au même endroit et sous le même nom que le script Python
(`Recordings/gif/<match>_round_XX.gif`).

L'encodage utilise **SixLabors.ImageSharp**, livrée à côté du DLL du mod —
`ModAssemblyLoadContext` résout les dépendances d'un mod dans son propre dossier,
donc `release.bat` copie cette DLL. Sans elle, l'export se désactive proprement en
journalisant l'erreur, sans casser l'enregistrement.

L'encodage tourne sur **son propre thread**, distinct de celui qui écrit les PNG :
un round de 277 images demande une quinzaine de secondes, pendant lesquelles le
round suivant continue d'être capturé. Bloquer le thread d'écriture remplirait sa
file de 240 frames et ferait dropper des images. Les PNG sont relus depuis le
disque plutôt que gardés en mémoire (85 Mo pour un round en RGBA).

Mesures sur un round réel de 277 images :

| Qualité | Palette | Cadence | Taille | Temps |
|---------|---------|---------|--------|-------|
| High | 256 | toutes | 4,4 Mo | 17 s |
| Medium | 128 | 1 sur 2 | 2,4 Mo | 7 s |
| Low | 48 | 1 sur 3 | 1,5 Mo | 5 s |

Contrairement au script Python, l'encodeur du mod utilise une **table de couleurs
locale** (une palette par image). L'encodeur GIF d'ImageSharp dégénère brutalement
en mode global au-delà d'environ 200 images : 200 images passent en 6 s, 225 ne
terminent pas en 150 s. En local, le round complet sort en 15 s. Le coût est
négligeable — 4,4 Mo contre 4,1 Mo pour Pillow, et le scintillement de palette
redouté ne représente que +0,13 point de pixels changeants entre images
consécutives par rapport aux PNG d'origine.

Le script ci-dessous reste le moyen de référence : il permet d'essayer plusieurs
réglages sur un enregistrement existant sans rejouer un match.

## GIF a posteriori : `tools/make_gif.py`

Copier le script dans le dossier `Recordings/` et le lancer sans argument : il
traite **tous** les dossiers `match_*` qu'il trouve à côté de lui et produit **un
GIF par round**. Les GIF déjà présents sont sautés, sauf avec `--force`.

```bash
python make_gif.py
```

Tous les GIF sont regroupés dans un dossier `gif/` placé **à côté** des dossiers de
match — pas parmi les PNG — et préfixés du nom du match pour rester identifiables
une fois rassemblés :

```
Recordings/
  gif/
    match_20260804_193244_round_00.gif
    match_20260804_193244_round_01.gif
    match_20260805_210114_round_00.gif
  match_20260804_193244/
    round_00_frame_000000.png
    ...
```

| Option | Rôle |
|--------|------|
| `--quality high\|medium\|low` | compromis taille/fidélité (défaut `high`) |
| `--colors N` | palette exacte (2-256), prioritaire sur `--quality` |
| `--every N` | ne garder qu'une image sur N, prioritaire sur `--quality` |
| `--fps`, `--scale`, `--no-loop`, `--force`, `-d` | vitesse, agrandissement, divers |

Mesures sur un round réel de 277 images :

| Qualité | Palette | Cadence | Taille |
|---------|---------|---------|--------|
| `high` | 256 | toutes | 4,1 Mo |
| `medium` | 128 | 1 sur 2 | 2,2 Mo |
| `low` | 48 | 1 sur 3 | 1,4 Mo |

Le levier utile est la **cadence**, pas la palette : passer de 256 à 48 couleurs ne
gagne que 18 %, alors que garder une image sur deux en gagne 42 %. `--every` allonge
la durée d'affichage en proportion, donc le round garde sa vitesse réelle.

Deux détails d'encodage qui pèsent lourd : une **palette commune** à tout le round
(sinon chaque image embarque la sienne et les couleurs scintillent), et
`disposal=1` au lieu de `2`, qui laisse Pillow ne stocker que les zones changeantes
— 43 % de gain, à rendu strictement identique puisque les images sont opaques et
couvrent tout l'écran.

## Vidéo : `ffmpeg`

Les index sont contigus **à l'intérieur** d'un round, donc `%06d` suffit ; seul le
round 0 démarre à 0, les suivants ont besoin de `-start_number` (index de leur
première image) :

```bash
ffmpeg -framerate 15 -i round_00_frame_%06d.png -c:v libx264 -pix_fmt yuv420p round_00.mp4
```

(adapter `-framerate` au FPS choisi)

## Performance

Le thread de rendu ne fait qu'une copie mémoire rapide (`GetData` des pixels) +
lecture des inputs/positions, puis un `Enqueue` non bloquant. L'**encodage PNG et
l'écriture disque tournent sur un thread de fond** (`RecorderWriter`). La grille de
solides est calculée **une seule fois par niveau**. Les buffers de pixels sont
recyclés (pool) pour limiter le GC.

L'encodeur PNG (`PngEncoder`) est écrit à la main, 100 % managé, sans
`System.Drawing` — cette assembly n'existe pas dans le runtime FortRise. Deux choix
non évidents, tous deux mesurés sur de vraies frames :

- **Filtre None** plutôt que Sub/Up/Paeth. Contre-intuitif, mais le rendu est un
  pavage de tuiles répétées à l'identique : brutes, elles restent des séquences
  d'octets identiques que LZ77 apparie à longue distance, alors que le filtrage
  par différence les rend dépendantes de leur voisinage et casse ces
  correspondances. Sortie **plus petite ET plus rapide** (une passe par octet en
  moins).
- **RGB au lieu de RGBA** quand la frame est opaque (toujours le cas en pratique) :
  un quart de données en moins, sans perte. L'alpha est vérifié pendant la
  conversion et le RGBA est repris automatiquement si une frame transparente sort.

Si le disque ne suit pas, la file est bornée (240 frames) et les frames
excédentaires sont droppées (comptées dans les logs) plutôt que de figer le jeu.

Seul coût réel côté rendu : `GetData` sur le RenderTarget force une petite synchro
GPU→CPU — négligeable au FPS de capture typique (identique à ce que fait le replay
du jeu).

## Build / déploiement

- `script/release.bat` — build → `release/`
- `script/deploy.bat` — copie vers le dossier Mods de TowerFall
- `script/release_deploy.bat` — les deux
