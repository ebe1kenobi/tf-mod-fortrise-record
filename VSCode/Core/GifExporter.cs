using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace TFModFortRiseRecord
{
  // Assemble les PNG d'un round en GIF anime, directement depuis le jeu.
  //
  // Toute la dependance a ImageSharp est confinee ici : le JIT ne cherche
  // l'assembly qu'au premier appel d'une methode de cette classe, ce qui permet a
  // GifExport de rattraper proprement son absence (voir la-bas). C'est exactement
  // le mecanisme qui avait fait planter SavePng avec System.Drawing.Common.
  //
  // Choix d'encodage : pas de tramage (bruit couteux et laid sur du pixel art),
  // NotDispose pour que l'encodeur ne stocke que les zones qui changent.
  //
  // Table de couleurs LOCALE, contrairement a tools/make_gif.py qui utilise une
  // palette globale : mesure sur un round reel, l'encodeur GIF d'ImageSharp
  // degenere brutalement en mode Global au-dela d'environ 200 images (200 images
  // = 6 s, 225 = plus de 150 s sans aboutir). En Local le meme round de 277
  // images sort en 15 s. Le surcout est negligeable : 4444 Ko contre 4126 Ko, et
  // le scintillement de palette redoute ne represente que +0,13 point de pixels
  // changeants entre images consecutives par rapport aux PNG d'origine.
  internal static class GifEncoderCore
  {
    public static long Encode(List<string> pngPaths, string outputPath,
                              int fps, int every, int colors)
    {
      if (every < 1) every = 1;

      // GIF : delai par image en centiemes de seconde. On l'allonge du facteur de
      // sous-echantillonnage pour que le round garde sa vitesse reelle.
      int delayCs = (int)Math.Round(100.0 * every / Math.Max(1, fps));
      if (delayCs < 2) delayCs = 2;

      Image<Rgba32> gif = null;
      try
      {
        for (int i = 0; i < pngPaths.Count; i += every)
        {
          Image<Rgba32> frame = Image.Load<Rgba32>(pngPaths[i]);
          try
          {
            if (gif == null)
            {
              gif = frame;
              frame = null; // possede par gif desormais
              GifMetadata meta = gif.Metadata.GetGifMetadata();
              meta.RepeatCount = 0; // 0 = boucle infinie
              meta.ColorTableMode = GifColorTableMode.Local;
              Configure(gif.Frames.RootFrame.Metadata.GetGifMetadata(), delayCs);
            }
            else
            {
              Configure(frame.Frames.RootFrame.Metadata.GetGifMetadata(), delayCs);
              gif.Frames.AddFrame(frame.Frames.RootFrame);
            }
          }
          finally
          {
            if (frame != null) frame.Dispose();
          }
        }

        if (gif == null) return 0;

        GifEncoder encoder = new GifEncoder
        {
          ColorTableMode = GifColorTableMode.Local,
          Quantizer = new WuQuantizer(new QuantizerOptions
          {
            MaxColors = colors,
            Dither = null,
          }),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        gif.SaveAsGif(outputPath, encoder);
        return new FileInfo(outputPath).Length;
      }
      finally
      {
        if (gif != null) gif.Dispose();
      }
    }

    private static void Configure(GifFrameMetadata meta, int delayCs)
    {
      meta.FrameDelay = delayCs;
      meta.DisposalMethod = GifDisposalMethod.NotDispose;
    }
  }
}
