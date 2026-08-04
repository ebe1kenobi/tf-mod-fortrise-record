using System.IO;
using System.IO.Compression;

namespace TFModFortRiseRecord
{
  // Encodeur PNG minimal, 100% managed (8 bits/canal, RGB ou RGBA, zlib via
  // ZLibStream). Volontairement sans System.Drawing : cette assembly n'existe pas
  // dans le runtime de FortRise et n'est pas deployee avec le mod, donc SavePng
  // levait une FileNotFoundException a la premiere frame.
  //
  // PNG est sans perte : le seul reglage est le compromis taille/CPU, aucune
  // option ne degrade l'image.
  internal static class PngEncoder
  {
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static readonly byte[] Empty = new byte[0];
    private static readonly uint[] CrcTable = BuildCrcTable();

    // pixels : w * h * channels octets, ordre R,G,B[,A].
    // channels : 3 (RGB) ou 4 (RGBA).
    public static void Write(string path, int w, int h, byte[] pixels, int channels, CompressionLevel level)
    {
      using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
      {
        fs.Write(Signature, 0, Signature.Length);

        byte[] ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)w);
        WriteBE(ihdr, 4, (uint)h);
        ihdr[8] = 8;                                  // profondeur : 8 bits par canal
        ihdr[9] = (byte)(channels == 4 ? 6 : 2);      // type couleur : RGBA ou RGB
        ihdr[10] = 0;                                 // compression : deflate
        ihdr[11] = 0;                                 // methode de filtrage standard
        ihdr[12] = 0;                                 // pas d'entrelacement
        WriteChunk(fs, "IHDR", ihdr, 0, ihdr.Length);

        byte[] idat = Compress(w, h, pixels, channels, level);
        WriteChunk(fs, "IDAT", idat, 0, idat.Length);

        WriteChunk(fs, "IEND", Empty, 0, 0);
      }
    }

    // Scanlines deflatees, chacune prefixee de son octet de filtre.
    //
    // Filtre 0 (None), contre-intuitif mais mesure sur de vraies frames : les
    // filtres par difference (Sub/Up/Paeth) coutent une passe par octet et
    // sortent PLUS gros ici. Le rendu est un pavage de tuiles repetees a
    // l'identique ; brutes, ces tuiles restent des sequences d'octets identiques
    // que LZ77 apparie a longue distance, alors que le filtrage les rend
    // dependantes de leur voisinage et casse ces correspondances.
    private static byte[] Compress(int w, int h, byte[] pixels, int channels, CompressionLevel level)
    {
      int stride = w * channels;
      using (MemoryStream ms = new MemoryStream(stride * h / 4 + 1024))
      {
        using (ZLibStream z = new ZLibStream(ms, level, true))
        {
          for (int y = 0; y < h; y++)
          {
            z.WriteByte(0);
            z.Write(pixels, y * stride, stride);
          }
        }
        return ms.ToArray();
      }
    }

    private static void WriteChunk(Stream s, string type, byte[] data, int offset, int count)
    {
      byte[] len = new byte[4];
      WriteBE(len, 0, (uint)count);
      s.Write(len, 0, 4);

      byte[] typeBytes = new byte[4];
      for (int i = 0; i < 4; i++)
        typeBytes[i] = (byte)type[i];
      s.Write(typeBytes, 0, 4);

      // Le CRC couvre le type puis les donnees, pas la longueur.
      uint crc = CrcUpdate(0xFFFFFFFFu, typeBytes, 0, 4);
      if (count > 0)
      {
        s.Write(data, offset, count);
        crc = CrcUpdate(crc, data, offset, count);
      }
      crc ^= 0xFFFFFFFFu;

      byte[] crcBytes = new byte[4];
      WriteBE(crcBytes, 0, crc);
      s.Write(crcBytes, 0, 4);
    }

    private static void WriteBE(byte[] buf, int offset, uint value)
    {
      buf[offset] = (byte)(value >> 24);
      buf[offset + 1] = (byte)(value >> 16);
      buf[offset + 2] = (byte)(value >> 8);
      buf[offset + 3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
      uint[] table = new uint[256];
      for (uint n = 0; n < 256; n++)
      {
        uint c = n;
        for (int k = 0; k < 8; k++)
          c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        table[n] = c;
      }
      return table;
    }

    private static uint CrcUpdate(uint c, byte[] buf, int offset, int count)
    {
      for (int i = 0; i < count; i++)
        c = CrcTable[(c ^ buf[offset + i]) & 0xFF] ^ (c >> 8);
      return c;
    }
  }
}
