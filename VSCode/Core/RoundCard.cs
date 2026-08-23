using System;
using MSColor = Microsoft.Xna.Framework.Color;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// La page noire qui annonce chaque manche dans un enregistrement de match.
  ///
  /// **Pourquoi des vraies images.** L'alternative etait d'afficher un carton dans le
  /// lecteur du mod. Mais un enregistrement se regarde de deux facons - le lecteur en
  /// jeu, et le GIF exporte - et un carton dessine par le lecteur n'existerait pas dans
  /// le GIF. En ecrivant de vraies frames PNG, la separation est DANS l'enregistrement :
  /// les deux la montrent, sans que ni l'un ni l'autre ait a la connaitre.
  ///
  /// **Le nommage fait tout le travail.** Les fichiers d'un match vivent dans un seul
  /// dossier et se lisent dans l'ordre alphabetique - "round_01_frame_000123.png". Une
  /// carte ecrite avec le prefixe de la manche qui COMMENCE et l'index courant tombe
  /// donc naturellement juste avant la premiere image de cette manche. Rien a trier,
  /// rien a insererer.
  ///
  /// **La police est dessinee ici, en dur.** On n'a pas de rendu de texte a cet endroit
  /// - on ecrit dans un tableau de pixels, hors du thread de rendu, sans peripherique
  /// graphique. Cinq lettres et dix chiffres en 3x5, agrandis, suffisent a ecrire
  /// "ROUND 12". C'est peu de lignes pour ne dependre de rien.
  /// </summary>
  internal static class RoundCard
  {
    /// <summary>Images que dure la carte. Une seconde et demie a quinze par seconde.</summary>
    public const int Frames = 22;

    /// <summary>Agrandissement de la police 3x5.</summary>
    private const int Scale = 4;

    /// <summary>Espace entre deux glyphes, en pixels de police.</summary>
    private const int Gap = 1;

    private static readonly MSColor Ink = new MSColor(240, 240, 245);
    private static readonly MSColor Back = new MSColor(0, 0, 0);

    /// <summary>
    /// Les glyphes, en 3 colonnes sur 5 rangees, un bit par pixel.
    ///
    /// Seulement ce qu'il faut pour ecrire "ROUND" et un nombre : cinq lettres et dix
    /// chiffres. Ajouter l'alphabet entier serait du decor inutilise.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<char, byte[]> Glyphs =
        new System.Collections.Generic.Dictionary<char, byte[]>
        {
          ['R'] = new byte[] { 0b110, 0b101, 0b110, 0b101, 0b101 },
          ['O'] = new byte[] { 0b111, 0b101, 0b101, 0b101, 0b111 },
          ['U'] = new byte[] { 0b101, 0b101, 0b101, 0b101, 0b111 },
          ['N'] = new byte[] { 0b101, 0b111, 0b111, 0b111, 0b101 },
          ['D'] = new byte[] { 0b110, 0b101, 0b101, 0b101, 0b110 },
          ['0'] = new byte[] { 0b111, 0b101, 0b101, 0b101, 0b111 },
          ['1'] = new byte[] { 0b010, 0b110, 0b010, 0b010, 0b111 },
          ['2'] = new byte[] { 0b111, 0b001, 0b111, 0b100, 0b111 },
          ['3'] = new byte[] { 0b111, 0b001, 0b111, 0b001, 0b111 },
          ['4'] = new byte[] { 0b101, 0b101, 0b111, 0b001, 0b001 },
          ['5'] = new byte[] { 0b111, 0b100, 0b111, 0b001, 0b111 },
          ['6'] = new byte[] { 0b111, 0b100, 0b111, 0b101, 0b111 },
          ['7'] = new byte[] { 0b111, 0b001, 0b010, 0b010, 0b010 },
          ['8'] = new byte[] { 0b111, 0b101, 0b111, 0b101, 0b111 },
          ['9'] = new byte[] { 0b111, 0b101, 0b111, 0b001, 0b111 },
          [' '] = new byte[] { 0, 0, 0, 0, 0 },
        };

    /// <summary>
    /// Peint "ROUND N" en blanc sur noir dans un tampon deja loue.
    ///
    /// Le tampon n'est PAS alloue ici : il vient du pool du writer, qui le rendra
    /// apres ecriture. En allouer un neuf a chaque carte contournerait le pool dont
    /// l'existence meme sert a ne pas allouer par image.
    /// </summary>
    public static void Paint(MSColor[] pixels, int width, int height, int roundNumber)
    {
      if (pixels == null || width <= 0 || height <= 0)
      {
        return;
      }

      int count = Math.Min(pixels.Length, width * height);

      for (int i = 0; i < count; i++)
      {
        pixels[i] = Back;
      }

      string text = "ROUND " + roundNumber;

      int glyphWidth = (3 + Gap) * Scale;
      int textWidth = text.Length * glyphWidth - Gap * Scale;
      int left = (width - textWidth) / 2;
      int top = (height - 5 * Scale) / 2;

      for (int c = 0; c < text.Length; c++)
      {
        if (!Glyphs.TryGetValue(text[c], out byte[] rows))
        {
          continue;
        }

        DrawGlyph(pixels, width, height, rows, left + c * glyphWidth, top);
      }
    }

    private static void DrawGlyph(MSColor[] pixels, int width, int height, byte[] rows,
        int left, int top)
    {
      for (int row = 0; row < 5; row++)
      {
        for (int col = 0; col < 3; col++)
        {
          // Le bit de poids fort est la colonne de GAUCHE : c'est ce qui rend les
          // litteraux binaires ci-dessus lisibles comme un dessin.
          if ((rows[row] & (1 << (2 - col))) == 0)
          {
            continue;
          }

          Fill(pixels, width, height,
              left + col * Scale, top + row * Scale, Scale, Scale);
        }
      }
    }

    private static void Fill(MSColor[] pixels, int width, int height,
        int x, int y, int w, int h)
    {
      for (int py = y; py < y + h; py++)
      {
        if (py < 0 || py >= height)
        {
          continue;
        }

        int rowStart = py * width;

        for (int px = x; px < x + w; px++)
        {
          if (px < 0 || px >= width)
          {
            continue;
          }

          int at = rowStart + px;

          if (at >= 0 && at < pixels.Length)
          {
            pixels[at] = Ink;
          }
        }
      }
    }
  }
}
