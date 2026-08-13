using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using TowerFall;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// Le dessin commun aux deux lecteurs : l'image, et le bandeau du bas.
  ///
  /// Toute la difficulte tient a une distinction : l'ECRAN et l'INTERFACE n'ont pas la
  /// meme taille des qu'un mod elargit le jeu.
  ///
  /// WiderSet porte l'ecran a 420 de large, mais laisse l'interface a ses 320 d'origine
  /// et la recentre en translatant de +50 la matrice des couches d'interface - c'est ce
  /// qui permet a tout le menu vanilla, ecrit pour du 320, de rester centre sans etre
  /// retouche. Une entite d'interface qui dessine en 0 se retrouve donc a 50 sur
  /// l'ecran, et c'est ce decalage que l'on voyait sur l'image.
  ///
  /// D'ou le partage :
  ///
  /// - le BANDEAU reste dans l'interface, 320x240. C'est le repere de tout le reste du
  ///   jeu, il est centre quoi qu'il arrive, et rien n'en sort. Le calculer sur l'ecran
  ///   avait justement fait sortir l'etat et le compteur du cadre visible.
  ///
  /// - l'IMAGE se place sur l'ecran, dont la largeur est deduite de l'image ELLE-MEME :
  ///   un enregistrement est une capture d'ecran entiere, donc sa largeur est celle de
  ///   l'ecran ou il a ete pris. Rien n'est demande a WiderSet ni au moteur, et le
  ///   calcul vaut aussi bien quand aucun mod n'elargit le jeu - l'image fait alors 320
  ///   et le decalage est nul.
  /// </summary>
  internal static class ReplayView
  {
    /// <summary>La taille de l'interface du jeu. Elle ne change pas.</summary>
    public const float UiWidth = 320f;

    public const float UiHeight = 240f;

    /// <summary>Hauteur du bandeau : deux lignes de texte.</summary>
    public const float BarHeight = 26f;

    /// <summary>
    /// Le fond noir, et l'image posee dessus.
    ///
    /// Le fond deborde volontairement de l'interface : sous WiderSet il reste cent
    /// pixels d'ecran de part et d'autre, et sans ce debord on verrait le niveau en
    /// train de tourner derriere la relecture.
    /// </summary>
    public static void DrawFrame(Texture2D frame)
    {
      // Large a dessein, sans chercher a coller a l'image : ce qui deborde ne se voit
      // pas, alors qu'un fond trop juste laisserait apparaitre le niveau en train de
      // tourner sur les cotes.
      Draw.Rect(-UiWidth, -UiHeight, UiWidth * 3f, UiHeight * 3f, Color.Black);

      if (frame == null)
      {
        return;
      }

      // L'enregistrement est une capture d'ecran entiere : sa largeur est celle de
      // l'ecran, et le bord gauche de l'ecran tombe donc a cette abscisse-la dans les
      // coordonnees de l'interface.
      float left = -(frame.Width - UiWidth) / 2f;
      float top = -(frame.Height - UiHeight) / 2f;

      Draw.SpriteBatch.Draw(frame, new Vector2(left, top), Color.White);
    }

    /// <summary>
    /// Le bandeau : le rappel des touches sur la premiere ligne, l'etat et le compteur
    /// aux deux bouts de la seconde.
    ///
    /// Deux lignes parce que sur une seule, le rappel centre passait par-dessus l'etat
    /// et le compteur.
    ///
    /// Tout est pose dans l'interface, en 320 de large. Le bandeau ne va donc pas d'un
    /// bord a l'autre de l'ecran sous WiderSet - mais il est entier, centre, et lisible,
    /// ce qui vaut mieux qu'un bandeau pleine largeur dont les deux bouts tombent hors
    /// du champ.
    /// </summary>
    public static void DrawHud(string hint, string left, string right, float alpha)
    {
      float top = UiHeight - BarHeight;

      Draw.Rect(0f, top, UiWidth, BarHeight, Color.Black * (0.7f * alpha));

      Draw.OutlineTextCentered(TFGame.Font, hint, new Vector2(UiWidth / 2f, top + 7f),
          Color.Gray * alpha, Color.Black * alpha, 1f);

      Draw.OutlineTextJustify(TFGame.Font, left, new Vector2(6f, top + 19f),
          Color.White * alpha, Color.Black * alpha, new Vector2(0f, 0.5f), 1f);

      Draw.OutlineTextJustify(TFGame.Font, right, new Vector2(UiWidth - 6f, top + 19f),
          Color.Gray * alpha, Color.Black * alpha, new Vector2(1f, 0.5f), 1f);
    }
  }
}
