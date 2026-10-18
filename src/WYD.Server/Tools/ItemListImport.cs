using WYD.Server.Data;

namespace WYD.Server.Tools;

/// <summary>
/// Traz a ItemList de outro client 7.54 para o formato do nosso. O registro de cada item é o mesmo (140 bytes,
/// 6500 itens); o que muda é a cifra: o Star Destiny troca o XOR 0x5A de cada byte por um XOR com um texto de
/// 512 bytes que se repete. Como a maior parte dos bytes de um item é zero, a chave aparece sozinha: para cada
/// período, o byte mais comum em cada posição. A lista volta para o XOR 0x5A e o checksum é acertado para o valor
/// que o client 7.54 exige (<see cref="ItemList.ClientChecksum"/>) mexendo só em itens vazios do fim da lista.
/// </summary>
public static class ItemListImport
{
    public sealed record Result(byte[] File, int KeyLength, int Items, int AdjustedBytes, int RemappedMeshes = 0);

    private const int Size = ItemDefinition.Size;
    private const int MaxPeriod = 2048;
    private const int Candidates = 6;
    private const byte OriginalXor = 0x5A;

    /// <summary>
    /// Converte; null se nenhuma chave decifra a lista. <paramref name="meshRemap"/> troca o modelo (Mesh) dos itens:
    /// o Star deu números novos a modelos que o nosso client já tem com outro número (ver
    /// <see cref="ClientImport.MeshRemap"/>), e o client trata alguns modelos pelo número.
    /// </summary>
    public static Result? Convert(byte[] source, IReadOnlyDictionary<short, short>? meshRemap = null)
    {
        if (source.Length < ItemList.DataSize) return null;
        var data = source.AsSpan(0, ItemList.DataSize).ToArray();

        byte[]? bestPlain = null;
        int bestScore = 0, bestPeriod = 0;
        foreach (int period in new[] { 1 }.Concat(LikelyPeriods(data)))
        {
            var key = period == 1 ? [OriginalXor] : KeyFor(data, period);
            var plain = new byte[data.Length];
            for (int i = 0; i < data.Length; i++) plain[i] = (byte)(data[i] ^ key[i % key.Length]);
            int score = ValidItems(plain);
            // Múltiplos do período certo empatam: fica o menor.
            if (score > bestScore || (score == bestScore && period < bestPeriod))
                (bestPlain, bestScore, bestPeriod) = (plain, score, period);
        }
        if (bestPlain is null || bestScore < MinValidItems) return null;

        int remapped = 0;
        if (meshRemap is { Count: > 0 })
            for (int item = 1; item < ItemList.MaxItems; item++)
            {
                var mesh = bestPlain.AsSpan(item * Size + 64, 2);
                if (bestPlain[item * Size] != 0 && meshRemap.TryGetValue(System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(mesh), out short to))
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(mesh, to);
                    remapped++;
                }
            }

        var encoded = new byte[ItemList.DataSize + 4];
        for (int i = 0; i < ItemList.DataSize; i++) encoded[i] = (byte)(bestPlain[i] ^ OriginalXor);
        int adjusted = FixChecksum(encoded.AsSpan(0, ItemList.DataSize));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(ItemList.DataSize),
            ItemList.ComputeChecksum(encoded.AsSpan(0, ItemList.DataSize)));
        return new Result(encoded, bestPeriod, bestScore, adjusted, remapped);
    }

    /// <summary>Uma lista de verdade tem milhares de itens com nome legível; uma chave errada quase nenhum.</summary>
    private const int MinValidItems = 500;

    /// <summary>Os períodos em que o arquivo mais se repete (os zeros cifrados com a mesma chave são iguais).</summary>
    private static IEnumerable<int> LikelyPeriods(byte[] data)
    {
        int sample = Math.Min(data.Length - MaxPeriod, 200_000);
        var scores = new List<(int Period, int Same)>();
        for (int p = 2; p <= MaxPeriod; p++)
        {
            int same = 0;
            for (int i = 0; i < sample; i++) if (data[i] == data[i + p]) same++;
            scores.Add((p, same));
        }
        return scores.OrderByDescending(s => s.Same).Take(Candidates).Select(s => s.Period);
    }

    private static byte[] KeyFor(byte[] data, int period)
    {
        var key = new byte[period];
        var counts = new int[256];
        for (int r = 0; r < period; r++)
        {
            Array.Clear(counts);
            for (int i = r; i < data.Length; i += period) counts[data[i]]++;
            key[r] = (byte)Array.IndexOf(counts, counts.Max());
        }
        return key;
    }

    /// <summary>Itens com nome de pelo menos 2 caracteres visíveis e o resto do campo do nome zerado.</summary>
    private static int ValidItems(byte[] plain)
    {
        int valid = 0;
        for (int i = 0; i < ItemList.MaxItems; i++)
        {
            var name = plain.AsSpan(i * Size, 64);
            int end = name.IndexOf((byte)0);
            if (end < 2) continue;
            bool ok = true;
            foreach (var b in name[..end]) if (b < 0x20) { ok = false; break; }
            if (ok && !name[end..].ContainsAnyExcept((byte)0)) valid++;
        }
        return valid;
    }

    /// <summary>
    /// O checksum do client (<see cref="ItemList.ComputeChecksum"/>) só subtrai o byte nas posições i % 9 == 5.
    /// Trocando esses bytes em itens vazios (de trás para frente, com o primeiro byte do nome em zero para o item
    /// continuar vazio) o checksum chega no valor exigido. Retorna quantos bytes mudaram.
    /// </summary>
    private static int FixChecksum(Span<byte> encoded)
    {
        long delta = (long)ItemList.ClientChecksum - ItemList.ComputeChecksum(encoded);
        int changed = 0;
        for (int item = ItemList.MaxItems - 1; item > 0 && delta != 0; item--)
        {
            var record = encoded.Slice(item * Size, Size);
            if (record.ContainsAnyExcept(OriginalXor)) continue;          // só itens vazios (tudo zero)
            for (int o = 1; o < Size && delta != 0; o++)
            {
                int i = item * Size + o;
                if (i % 9 != 5) continue;
                int b = (sbyte)encoded[i];
                // O byte passa a valer b - step (de -128 a 127) e o checksum sobe step.
                int step = (int)Math.Clamp(delta, b - 127, b + 128);
                encoded[i] = (byte)(sbyte)(b - step);
                delta -= step;
                changed++;
            }
        }
        if (delta != 0) throw new InvalidDataException("Não há itens vazios suficientes para acertar o checksum.");
        return changed;
    }
}
