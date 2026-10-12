namespace WYD.Server.Network;

/// <summary>
/// Cifra dos pacotes do WYD. Os 4 primeiros bytes (Size, KeyWord, CheckSum) trafegam em claro;
/// do byte 4 em diante cada byte é deslocado por uma chave da KeyTable, conforme a posição (i &amp; 3).
/// </summary>
public static class PacketCipher
{
    /// <summary>Cifra o pacote no lugar, preenchendo KeyWord e CheckSum. Retorna o CheckSum.</summary>
    public static byte Encrypt(Span<byte> packet, KeyTable table, byte keyWord)
    {
        int size = PacketHeader.ReadSize(packet);
        packet[PacketHeader.KeyWordOffset] = keyWord;

        int position = table.Increment(keyWord);
        byte plainSum = 0, cipherSum = 0;

        for (int i = PacketHeader.EncryptedStart; i < size; i++, position++)
        {
            int key = table.Key(position);
            plainSum += packet[i];
            packet[i] = (i & 3) switch
            {
                0 => (byte)(packet[i] + (key << 1)),
                1 => (byte)(packet[i] - (key >> 3)),
                2 => (byte)(packet[i] + (key << 2)),
                _ => (byte)(packet[i] - (key >> 5)),
            };
            cipherSum += packet[i];
        }

        byte checkSum = (byte)(cipherSum - plainSum);
        packet[PacketHeader.CheckSumOffset] = checkSum;
        return checkSum;
    }

    /// <summary>
    /// Decifra o pacote no lugar. Retorna true se o CheckSum confere.
    /// Como diferentes versões do client calculam o CheckSum com sinais opostos, as duas formas são aceitas.
    /// </summary>
    public static bool Decrypt(Span<byte> packet, KeyTable table)
    {
        int size = PacketHeader.ReadSize(packet);
        byte keyWord = packet[PacketHeader.KeyWordOffset];
        byte expected = packet[PacketHeader.CheckSumOffset];

        int position = table.Increment(keyWord);
        byte cipherSum = 0, plainSum = 0;

        for (int i = PacketHeader.EncryptedStart; i < size; i++, position++)
        {
            int key = table.Key(position);
            cipherSum += packet[i];
            packet[i] = (i & 3) switch
            {
                0 => (byte)(packet[i] - (key << 1)),
                1 => (byte)(packet[i] + (key >> 3)),
                2 => (byte)(packet[i] - (key << 2)),
                _ => (byte)(packet[i] + (key >> 5)),
            };
            plainSum += packet[i];
        }

        return expected == (byte)(cipherSum - plainSum) || expected == (byte)(plainSum - cipherSum);
    }
}
