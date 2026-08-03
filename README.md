# tf-mod-fortrise-record

Mod FortRise 4 pour TowerFall : enregistre un match complet (images + inputs +
état de jeu), en s'appuyant sur le même buffer de rendu que le système de replay
du jeu (`Engine.Instance.Screen.RenderTarget`).

## Réglages (mod settings)

| Option | Défaut | Rôle |
|--------|--------|------|
| Enable recording | off | Interrupteur maître |
| Captures per second | 15 | FPS de capture (commun images/inputs/état) |
| Record images (PNG) | on | Séquence d'images PNG 320×240 |
| Record player inputs | on | Inputs de chaque joueur par frame |
| Record game state (positions/grid) | on | Positions joueurs/flèches + grille de niveau |

L'enregistrement démarre à l'entrée en gameplay (`Level`) et s'arrête au retour au
menu / à la carte. **Un match = un dossier** :
`Documents/TowerFall/Recordings/match_<horodatage>/`.

## Sorties

- `frame_000000.png`, … — images 320×240 (résolution interne du jeu).
- `inputs.jsonl` — une ligne JSON par frame :
  `{"f":<frame>,"p":[{"i":<player>,"mx":,"my":,"ax":,"ay":,"jump":,"shoot":,"alt":,"dodge":,"arrows":}]}`
  (mx/my = direction, ax/ay = visée, les autres = boutons maintenus 0/1).
- `state.jsonl` — une ligne JSON par frame :
  `{"f":<frame>,"players":[{"i":,"x":,"y":,"vx":,"vy":,"face":,"aim":,"ground":,"dead":}],"arrows":[{"x":,"y":,"vx":,"vy":,"dir":,"st":,"pi":}]}`
- `level_<frame>.json` — grille de solides du niveau (calculée une fois par niveau) :
  `{"width":32,"height":24,"block":10,"grid":[[0,1,…],…]}` (1 = solide).

Tout est aligné par le champ `"f"` (index de frame), commun aux images et aux JSONL.

Reconstruire une vidéo :

```bash
ffmpeg -framerate 15 -i frame_%06d.png -c:v libx264 -pix_fmt yuv420p match.mp4
```

(adapter `-framerate` au FPS choisi)

## Performance

Le thread de rendu ne fait qu'une copie mémoire rapide (`GetData` des pixels) +
lecture des inputs/positions, puis un `Enqueue` non bloquant. L'**encodage PNG et
l'écriture disque tournent sur un thread de fond** (`RecorderWriter`). La grille de
solides est calculée **une seule fois par niveau**. Les buffers de pixels sont
recyclés (pool) pour limiter le GC.

Si le disque ne suit pas, la file est bornée (240 frames) et les frames
excédentaires sont droppées (comptées dans les logs) plutôt que de figer le jeu.

Seul coût réel côté rendu : `GetData` sur le RenderTarget force une petite synchro
GPU→CPU — négligeable au FPS de capture typique (identique à ce que fait le replay
du jeu).

## Build / déploiement

- `script/release.bat` — build → `release/`
- `script/deploy.bat` — copie vers le dossier Mods de TowerFall
- `script/release_deploy.bat` — les deux
