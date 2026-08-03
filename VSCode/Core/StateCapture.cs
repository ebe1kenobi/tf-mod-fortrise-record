using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;

namespace TFModFortRiseRecord
{
  // Construit les representations textuelles (JSON compact, une ligne par frame)
  // des inputs et de l'etat de jeu, ainsi que la grille de solides du niveau.
  // Tout est lu sur le thread de rendu (rapide) ; l'ecriture part au thread de fond.
  internal static class StateCapture
  {
    private const int BlockSize = 10;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string F(float v)
    {
      return v.ToString("0.##", Inv);
    }

    // Inputs de chaque joueur actif (etat maintenu de la frame).
    public static string BuildInputsLine(int frameIndex)
    {
      StringBuilder sb = new StringBuilder(256);
      sb.Append("{\"f\":").Append(frameIndex).Append(",\"p\":[");
      bool first = true;
      int count = TFGame.Players.Length;
      for (int i = 0; i < count; i++)
      {
        if (!TFGame.Players[i]) continue;
        if (i >= TFGame.PlayerInputs.Length || TFGame.PlayerInputs[i] == null) continue;
        InputState s = TFGame.PlayerInputs[i].GetState();
        if (!first) sb.Append(',');
        first = false;
        sb.Append("{\"i\":").Append(i)
          .Append(",\"mx\":").Append(s.MoveX)
          .Append(",\"my\":").Append(s.MoveY)
          .Append(",\"ax\":").Append(F(s.AimAxis.X))
          .Append(",\"ay\":").Append(F(s.AimAxis.Y))
          .Append(",\"jump\":").Append(s.JumpCheck ? 1 : 0)
          .Append(",\"shoot\":").Append(s.ShootCheck ? 1 : 0)
          .Append(",\"alt\":").Append(s.AltShootCheck ? 1 : 0)
          .Append(",\"dodge\":").Append(s.DodgeCheck ? 1 : 0)
          .Append(",\"arrows\":").Append(s.ArrowsPressed ? 1 : 0)
          .Append('}');
      }
      sb.Append("]}");
      return sb.ToString();
    }

    // Etat dynamique : positions/vitesses des joueurs et des fleches.
    public static string BuildStateLine(int frameIndex, Level level)
    {
      StringBuilder sb = new StringBuilder(512);
      sb.Append("{\"f\":").Append(frameIndex).Append(",\"players\":[");
      bool first = true;
      foreach (Entity e in level.Players)
      {
        Player p = e as Player;
        if (p == null) continue;
        if (!first) sb.Append(',');
        first = false;
        sb.Append("{\"i\":").Append(p.PlayerIndex)
          .Append(",\"x\":").Append(F(p.X))
          .Append(",\"y\":").Append(F(p.Y))
          .Append(",\"vx\":").Append(F(p.Speed.X))
          .Append(",\"vy\":").Append(F(p.Speed.Y))
          .Append(",\"face\":").Append((int)p.Facing)
          .Append(",\"aim\":").Append(F(p.AimDirection))
          .Append(",\"ground\":").Append(p.OnGround ? 1 : 0)
          .Append(",\"dead\":").Append(p.Dead ? 1 : 0)
          .Append('}');
      }
      sb.Append("],\"arrows\":[");
      first = true;
      foreach (Entity e in level[GameTags.Arrow])
      {
        Arrow a = e as Arrow;
        if (a == null) continue;
        if (!first) sb.Append(',');
        first = false;
        sb.Append("{\"x\":").Append(F(a.X))
          .Append(",\"y\":").Append(F(a.Y))
          .Append(",\"vx\":").Append(F(a.Speed.X))
          .Append(",\"vy\":").Append(F(a.Speed.Y))
          .Append(",\"dir\":").Append(F(a.Direction))
          .Append(",\"st\":").Append((int)a.State)
          .Append(",\"pi\":").Append(a.PlayerIndex)
          .Append('}');
      }
      sb.Append("]}");
      return sb.ToString();
    }

    // Grille de solides du niveau, calculee UNE fois par niveau (geometrie statique).
    // 1 = solide, 0 = vide. Meme echantillonnage que la grille de l'IA (centre de case).
    // Niveau standard TowerFall : 320x240 px = 32x24 cases de 10px.
    public static string BuildLevelGridJson(Level level)
    {
      const int cols = 32;
      const int rows = 24;

      StringBuilder sb = new StringBuilder(4096);
      sb.Append("{\"width\":").Append(cols)
        .Append(",\"height\":").Append(rows)
        .Append(",\"block\":").Append(BlockSize)
        .Append(",\"grid\":[");
      for (int y = 0; y < rows; y++)
      {
        if (y > 0) sb.Append(',');
        sb.Append('[');
        for (int x = 0; x < cols; x++)
        {
          if (x > 0) sb.Append(',');
          float wx = x * BlockSize + BlockSize * 0.5f;
          float wy = y * BlockSize + BlockSize * 0.5f;
          bool solid = level.CollideCheck(new Vector2(wx, wy), GameTags.Solid);
          sb.Append(solid ? '1' : '0');
        }
        sb.Append(']');
      }
      sb.Append("]}");
      return sb.ToString();
    }
  }
}
