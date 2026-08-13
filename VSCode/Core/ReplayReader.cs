using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// Un enregistrement sur le disque : ses images, et de quoi les lire une par une
  /// sans tout charger.
  ///
  /// Le calcul qui decide de tout : une image fait 320x240 en RGBA, soit 300 Ko une
  /// fois decodee. Un match de trois manches en compte facilement deux mille - six
  /// cents megaoctets. Precharger est donc exclu, et il faut LIRE AU FIL.
  ///
  /// Le travail est coupe en deux parce que les deux moities n'ont pas les memes
  /// contraintes :
  ///
  /// - la LECTURE du fichier est de l'attente disque pure, elle part sur un fil de
  ///   fond qui prend de l'avance ;
  /// - le DECODAGE en texture doit se faire sur le fil principal, la carte graphique
  ///   n'appartenant qu'a lui.
  ///
  /// Entre les deux, une petite file d'octets. Elle suffit a absorber un disque lent
  /// sans jamais tenir plus d'une poignee d'images en memoire.
  /// </summary>
  public sealed class ReplayReader : IDisposable
  {
    /// <summary>Images lues d'avance. Une demi-seconde de jeu.</summary>
    private const int LOOKAHEAD = 30;

    private readonly string[] files;
    private readonly Queue<(int Index, byte[] Data)> ready = new Queue<(int, byte[])>();
    private readonly object gate = new object();

    private Thread loader;
    private volatile bool stopping;

    /// <summary>L'image demandee par le dernier saut.</summary>
    private int wanted;

    /// <summary>
    /// Le numero du saut courant, incremente a chaque <see cref="SeekTo"/>.
    ///
    /// C'est lui qui distingue "le fil doit repartir d'ici" de "le fil a deja repris
    /// et avance normalement". Sans ce numero, la seule marque d'un saut etait l'ecart
    /// entre la position voulue et la position lue - un ecart qui ne disparait jamais,
    /// puisque la lecture s'eloigne du point de saut des l'image suivante. Le fil se
    /// croyait donc perpetuellement en retard et se replacait sur le point de saut des
    /// que la file se vidait.
    /// </summary>
    private int generation;

    /// <summary>La texture affichee, et le rang de l'image qu'elle porte.</summary>
    public Texture2D Frame { get; private set; }

    public int FrameIndex { get; private set; } = -1;

    public int Count => files.Length;

    public string Name { get; }

    /// <summary>
    /// La cadence a laquelle ces images ont ete prises.
    ///
    /// Elle n'est pas celle du jeu : le mod filme a la cadence reglee, quinze par
    /// defaut. C'est elle qui donne son sens a la vitesse x1 et aux sauts exprimes en
    /// secondes.
    /// </summary>
    public int Fps { get; }

    private ReplayReader(string directory, string[] files)
    {
      Name = Path.GetFileName(directory);
      this.files = files;
      Fps = ReadFps(directory);
    }

    /// <summary>
    /// Relit la cadence notee a l'enregistrement, ou 15 pour les enregistrements
    /// anterieurs a cette note - c'est la valeur par defaut du mod, donc celle qu'ils
    /// ont presque surement.
    /// </summary>
    internal static int ReadFps(string directory)
    {
      try
      {
        string path = Path.Combine(directory, MatchRecorder.FpsFileName);

        if (File.Exists(path) &&
            int.TryParse(File.ReadAllText(path).Trim(), out int fps) && fps >= 1)
        {
          return fps;
        }
      }
      catch (Exception)
      {
      }

      return 15;
    }

    /// <summary>
    /// Ouvre un enregistrement, ou rend null s'il ne contient aucune image.
    ///
    /// Les fichiers sont tries par NOM et non par date : ils s'appellent
    /// round_00_frame_000123.png, donc l'ordre alphabetique est l'ordre du match,
    /// manches comprises. La date de modification, elle, ne dit rien - le mod les
    /// ecrit depuis un fil de fond, dans un ordre qui n'est pas le leur.
    /// </summary>
    public static ReplayReader Open(string directory)
    {
      return Open(directory, null);
    }

    /// <summary>
    /// Ouvre une seule MANCHE d'un enregistrement, celle dont les fichiers portent ce
    /// prefixe ("round_01_"). Null pour tout le match.
    /// </summary>
    public static ReplayReader Open(string directory, string roundPrefix)
    {
      try
      {
        string pattern = (roundPrefix ?? "") + "*frame_*.png";
        string[] files = Directory.GetFiles(directory, pattern);

        if (files.Length == 0)
        {
          return null;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return new ReplayReader(directory, files);
      }
      catch (Exception e)
      {
        Logger.Info("[Replay] " + directory + " illisible : " + e.Message);
        return null;
      }
    }

    public void Start()
    {
      stopping = false;
      wanted = 0;

      loader = new Thread(Pump) { IsBackground = true, Name = "replay-reader" };
      loader.Start();
    }

    /// <summary>
    /// Demande a se placer sur cette image. Le fil de fond repart de la, et ce qui
    /// avait ete lu d'avance est jete : apres un saut, il ne sert plus a rien.
    /// </summary>
    public void SeekTo(int index)
    {
      index = Math.Clamp(index, 0, files.Length - 1);

      lock (gate)
      {
        wanted = index;
        generation++;
        ready.Clear();
        Monitor.PulseAll(gate);
      }
    }

    /// <summary>
    /// Prend l'image suivante si elle est prete, et la met dans <see cref="Frame"/>.
    /// Rend faux quand rien n'est pret - l'appelant garde alors l'image precedente
    /// plutot que d'afficher du noir.
    /// </summary>
    public bool Advance()
    {
      byte[] data;
      int index;

      lock (gate)
      {
        if (ready.Count == 0)
        {
          return false;
        }

        (index, data) = ready.Dequeue();
        Monitor.PulseAll(gate);
      }

      try
      {
        using var stream = new MemoryStream(data);
        Texture2D texture = Texture2D.FromStream(Engine.Instance.GraphicsDevice, stream);

        Frame?.Dispose();
        Frame = texture;
        FrameIndex = index;
        return true;
      }
      catch (Exception e)
      {
        Logger.Info("[Replay] image " + index + " indecodable : " + e.Message);
        return false;
      }
    }

    /// <summary>Vrai quand la derniere image a ete montree.</summary>
    public bool AtEnd => FrameIndex >= files.Length - 1;

    /// <summary>
    /// Le fil de fond : lit les fichiers en avance et s'arrete des que la file est
    /// pleine. Il ne decode rien - il n'a pas le droit de toucher a la carte
    /// graphique - il ne fait que sortir les octets du disque.
    /// </summary>
    private void Pump()
    {
      int seen = -1;
      int next = 0;

      while (!stopping)
      {
        int target;

        lock (gate)
        {
          // Un saut a ete demande depuis le dernier tour : on repart de la. Le test
          // porte sur le NUMERO du saut et non sur la position, donc il n'est vrai
          // qu'une fois - ensuite la lecture avance normalement.
          if (seen != generation)
          {
            seen = generation;
            next = wanted;
          }

          while (!stopping && seen == generation && ready.Count >= LOOKAHEAD)
          {
            Monitor.Wait(gate, 50);
          }

          // Un saut est arrive pendant l'attente : ce qu'on s'appretait a lire ne vaut
          // plus rien, on recommence le tour.
          if (seen != generation)
          {
            continue;
          }

          target = next;
        }

        if (stopping || target >= files.Length)
        {
          Thread.Sleep(20);
          continue;
        }

        byte[] data;

        try
        {
          data = File.ReadAllBytes(files[target]);
        }
        catch (Exception)
        {
          // Un fichier en cours d'ecriture, ou efface entre-temps : on saute et on
          // continue plutot que d'arreter la lecture.
          next = target + 1;
          continue;
        }

        lock (gate)
        {
          // Le disque a repondu apres un saut : cette image est celle d'avant le saut.
          // La mettre dans la file la ferait afficher, et le lecteur reviendrait a la
          // position qu'on vient justement de quitter.
          if (seen != generation)
          {
            continue;
          }

          ready.Enqueue((target, data));
          next = target + 1;
          Monitor.PulseAll(gate);
        }
      }
    }

    public void Dispose()
    {
      stopping = true;

      lock (gate)
      {
        Monitor.PulseAll(gate);
      }

      loader?.Join(200);

      Frame?.Dispose();
      Frame = null;

      lock (gate)
      {
        ready.Clear();
      }
    }
  }
}
