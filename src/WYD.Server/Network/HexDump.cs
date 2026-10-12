using System.Text;

namespace WYD.Server.Network;

public static class HexDump
{
    /// <summary>Formata bytes como "offset  hex  ascii", 16 por linha.</summary>
    public static string Format(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder();
        for (int line = 0; line < data.Length; line += 16)
        {
            var chunk = data.Slice(line, Math.Min(16, data.Length - line));
            sb.Append(line.ToString("X4")).Append("  ");
            for (int i = 0; i < 16; i++)
                sb.Append(i < chunk.Length ? chunk[i].ToString("X2") + " " : "   ");
            sb.Append(' ');
            foreach (var b in chunk)
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            if (line + 16 < data.Length) sb.AppendLine();
        }
        return sb.ToString();
    }
}
