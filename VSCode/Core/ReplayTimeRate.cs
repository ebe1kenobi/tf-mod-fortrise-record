using System;
using Monocle;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// Rend au jeu sa vitesse normale pendant une relecture, et la lui reprend apres.
  ///
  /// **Le mod accelerate leve <c>Engine.TimeRate</c>**, et il le fait justement la ou
  /// l'on regarde une relecture : a l'ecran de resultats de manche, a celui de fin de
  /// match, et a la revanche. Tout ce qui se mesure en temps de jeu se retrouve alors
  /// multiplie par deux ou par trois - c'est le but, pour les ecrans de resultats ; ce
  /// n'en est pas un pour une relecture, qui doit montrer ce qui s'est passe a la
  /// vitesse ou cela s'est passe.
  ///
  /// La cadence des images, elle, ne depend plus de ce reglage : <see cref="ReplayClock"/>
  /// compte sur l'horloge et non sur le temps de jeu. Mais tout le RESTE de la scene
  /// derriere la fenetre - le niveau, ses particules, ses animations - continuait de
  /// defiler au triple, et la fenetre elle-meme repetait les touches trois fois trop
  /// vite. On remet donc le temps a un pour la duree de la relecture.
  ///
  /// **On restitue la valeur TROUVEE, pas la valeur un.** Ce n'est pas la meme chose :
  /// accelerate pose son reglage a des moments precis, et rendre un plutot que ce qu'on
  /// a pris annulerait silencieusement son travail sur l'ecran d'ou l'on vient.
  /// </summary>
  internal struct ReplayTimeRate
  {
    private float taken;
    private bool held;

    public void Hold()
    {
      if (held)
      {
        return;
      }

      taken = Engine.TimeRate;
      held = true;

      if (Math.Abs(taken - 1f) < 0.001f)
      {
        return;
      }

      Engine.TimeRate = 1f;
      Logger.Info($"[Replay] temps de jeu ramene de x{taken:0.##} a x1 pour la relecture");
    }

    public void Release()
    {
      if (!held)
      {
        return;
      }

      held = false;

      if (Math.Abs(taken - 1f) < 0.001f)
      {
        return;
      }

      Engine.TimeRate = taken;
      Logger.Info($"[Replay] temps de jeu rendu a x{taken:0.##}");
    }
  }
}
