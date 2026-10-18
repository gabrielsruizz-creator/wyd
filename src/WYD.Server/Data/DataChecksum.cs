namespace WYD.Server.Data;

/// <summary>
/// Checksum que o client confere na ItemList.bin e na SkillData.bin (função 0x539CDB no WYD.exe 7.57, a mesma do
/// 7.54), feito sobre os bytes ainda codificados. Se não der o valor que o exe espera, o client para com
/// "Initialize Data Failed".
/// </summary>
public static class DataChecksum
{
    public static int Compute(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            int b = (sbyte)data[i];
            int m = i % 9;
            if (m == 0) sum += b * 2;
            if (m == 1) sum += b ^ 0xFF;
            if (m == 2) sum += b / 3;
            if (m == 3) sum += b * 2;
            if (m == 4) sum -= b ^ 0x5A;
            if (m == 5) sum -= b;
            else sum += b / 5;
        }
        return sum;
    }

    /// <summary>
    /// Leva o checksum ao valor esperado mexendo só em registros vazios (todos os bytes = <paramref name="empty"/>,
    /// o zero codificado), do fim para o começo: os bytes nas posições i % 9 == 5 só entram subtraindo, então cada
    /// um ajusta até 255. O primeiro byte de cada registro fica como está (nome vazio = item vazio). Retorna
    /// quantos bytes mudaram; sem registros vazios suficientes, InvalidDataException.
    /// </summary>
    public static int Fix(Span<byte> data, int recordSize, int expected, byte empty)
    {
        long delta = (long)expected - Compute(data);
        int changed = 0;
        for (int record = data.Length / recordSize - 1; record > 0 && delta != 0; record--)
        {
            if (data.Slice(record * recordSize, recordSize).ContainsAnyExcept(empty)) continue;
            for (int o = 1; o < recordSize && delta != 0; o++)
            {
                int i = record * recordSize + o;
                if (i % 9 != 5) continue;
                int b = (sbyte)data[i];
                // O byte passa a valer b - step (de -128 a 127) e o checksum sobe step.
                int step = (int)Math.Clamp(delta, b - 127, b + 128);
                data[i] = (byte)(sbyte)(b - step);
                delta -= step;
                changed++;
            }
        }
        if (delta != 0) throw new InvalidDataException("Não há registros vazios suficientes para acertar o checksum.");
        return changed;
    }
}
