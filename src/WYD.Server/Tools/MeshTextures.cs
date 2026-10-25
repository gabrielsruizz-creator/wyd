using System.Text;

namespace WYD.Server.Tools;

/// <summary>
/// mesh\MeshTextureList.bin do client 7.57: 2048 registros de 0x108 bytes, com o nome da textura terminado em zero, o
/// resto 0xCD e o tipo da textura no byte 0xFF ('N', 'A' ou 'C'; lido em 0x4BEA08 e 0x4CAD3F).
/// <para>
/// As armas novas (modelos 2889 a 2901, os "aa*.msa") ganham o brilho de refinação e de ancient numa segunda passada
/// (0x4CAB5A), que o client pula quando a textura da arma é do tipo 'C' (0x4CAD4D). Todas as texturas "aa" da lista
/// original são 'A', menos a do Hrotti (aawswordtwo.wys, registro 1683), que veio 'C': por isso ela é a única arma
/// nova sem brilho.
/// </para>
/// </summary>
public static class MeshTextures
{
    public const int RecordSize = 0x108, Records = 0x800;
    public const int TypeOffset = 0xFF;
    public const byte TypeAlpha = (byte)'A', TypeNoGlow = (byte)'C';

    public static string Name(byte[] list, int index)
    {
        var r = list.AsSpan(index * RecordSize, RecordSize);
        int end = r.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? r : r[..end]);
    }

    /// <summary>Passa para 'A' as texturas das armas novas ("mesh\aa...") marcadas como 'C'. Retorna os nomes corrigidos.</summary>
    public static List<string> FixGlow(byte[] list)
    {
        var fixedNames = new List<string>();
        for (int i = 0; i < Records && (i + 1) * RecordSize <= list.Length; i++)
        {
            string name = Name(list, i);
            int type = i * RecordSize + TypeOffset;
            if (!name.StartsWith("mesh\\aa", StringComparison.OrdinalIgnoreCase) || list[type] != TypeNoGlow) continue;
            list[type] = TypeAlpha;
            fixedNames.Add(name);
        }
        return fixedNames;
    }
}
