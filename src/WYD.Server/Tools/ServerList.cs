using System.Text;
using System.Text.RegularExpressions;

namespace WYD.Server.Tools;

/// <summary>
/// Lê e edita o serverlist.bin do client. O arquivo guarda os endereços dos servidores em campos de
/// tamanho fixo, quase todos preenchidos com zeros, e é codificado com uma chave que se repete.
/// Como os zeros codificados revelam a chave, ela é descoberta automaticamente: para cada período
/// possível, o byte mais comum em cada posição é a chave, e fica o período que decodifica para texto.
/// </summary>
public sealed partial class ServerList
{
    private const int MaxPeriod = 256;
    private const int MinSamplesPerKeyByte = 16;

    private readonly byte[] _key;
    private readonly bool _xor;

    private ServerList(byte[] key, bool xor, double score)
    {
        _key = key;
        _xor = xor;
        Score = score;
    }

    /// <summary>Fração de bytes decodificados que são zero ou caracteres de endereço (1.0 = arquivo todo legível).</summary>
    public double Score { get; }
    public int Period => _key.Length;
    public string Method => _xor ? "xor" : "soma";

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex Ipv4();

    public static ServerList Detect(byte[] data)
    {
        ServerList? best = null;
        int maxPeriod = Math.Clamp(data.Length / MinSamplesPerKeyByte, 1, MaxPeriod);

        for (int period = 1; period <= maxPeriod; period++)
        {
            var key = new byte[period];
            for (int r = 0; r < period; r++)
            {
                var counts = new int[256];
                for (int i = r; i < data.Length; i += period) counts[data[i]]++;
                key[r] = (byte)Array.IndexOf(counts, counts.Max());
            }

            foreach (bool xor in new[] { false, true })
            {
                var candidate = new ServerList(key, xor, ScoreOf(Decode(data, key, xor)));
                // Só troca por um período maior se ele for claramente melhor (múltiplos do certo empatam).
                if (best is null || candidate.Score > best.Score + 0.002)
                    best = candidate;
            }
        }

        return best!;
    }

    public byte[] Decode(byte[] data) => Decode(data, _key, _xor);

    public byte[] Encode(byte[] plain)
    {
        var data = new byte[plain.Length];
        for (int i = 0; i < plain.Length; i++)
        {
            byte k = _key[i % _key.Length];
            data[i] = _xor ? (byte)(plain[i] ^ k) : (byte)(plain[i] + k);
        }
        return data;
    }

    private static byte[] Decode(byte[] data, byte[] key, bool xor)
    {
        var plain = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            byte k = key[i % key.Length];
            plain[i] = xor ? (byte)(data[i] ^ k) : (byte)(data[i] - k);
        }
        return plain;
    }

    private static double ScoreOf(byte[] plain)
    {
        // Conta zeros e caracteres de endereço (letras, números, . : / - _), não qualquer texto,
        // para desempatar entre soma e xor quando os dois dão bytes "imprimíveis".
        int good = 0;
        foreach (var b in plain)
            if (b == 0 || char.IsAsciiLetterOrDigit((char)b) || b is (byte)'.' or (byte)':' or (byte)'/' or (byte)'-' or (byte)'_') good++;
        return plain.Length == 0 ? 0 : (double)good / plain.Length;
    }

    /// <summary>Trechos de texto (4+ caracteres) do arquivo decodificado, com a posição de cada um.</summary>
    public static List<(int Offset, string Text)> Strings(byte[] plain)
    {
        var result = new List<(int, string)>();
        int i = 0;
        while (i < plain.Length)
        {
            if (plain[i] is < 0x21 or >= 0x7F) { i++; continue; }
            int start = i;
            while (i < plain.Length && plain[i] is >= 0x20 and < 0x7F) i++;
            if (i - start >= 4)
                result.Add((start, Encoding.ASCII.GetString(plain, start, i - start)));
        }
        return result;
    }

    /// <summary>Troca todos os IPs do arquivo decodificado por <paramref name="newIp"/>. Retorna quantos trocou.</summary>
    public static int ReplaceIps(byte[] plain, string newIp)
    {
        int replaced = 0;

        foreach (var (offset, text) in Strings(plain))
        {
            var ip = Ipv4().Match(text);
            if (!ip.Success) continue;

            string newText = text[..ip.Index] + newIp + text[(ip.Index + ip.Length)..];
            int textEnd = offset + text.Length;

            // Se o texto novo for maior, precisa caber nos zeros depois do antigo, deixando um zero no fim.
            int free = 0;
            while (textEnd + free < plain.Length && plain[textEnd + free] == 0) free++;
            if (newText.Length - text.Length > free - 1)
                throw new InvalidOperationException($"O IP '{newIp}' não cabe no lugar de '{ip.Value}' (posição {offset}).");

            plain.AsSpan(offset, Math.Max(text.Length, newText.Length)).Clear();
            Encoding.ASCII.GetBytes(newText).CopyTo(plain, offset);
            replaced++;
        }

        return replaced;
    }
}
