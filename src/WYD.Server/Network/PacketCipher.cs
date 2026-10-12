namespace WYD.Server.Network;

/// <summary>
/// Como o CheckSum é calculado. As duas formas aparecem em versões diferentes do jogo; o client
/// confere os pacotes que recebe do mesmo jeito que calcula os que envia.
/// </summary>
public enum ChecksumMode
{
    /// <summary>soma(cifrado) - soma(em claro)</summary>
    CipherMinusPlain,
    /// <summary>soma(em claro) - soma(cifrado)</summary>
    PlainMinusCipher,
}

/// <summary>
/// Cifra dos pacotes do WYD. Os 4 primeiros bytes (Size, KeyWord, CheckSum) trafegam em claro;
/// do byte 4 em diante cada byte é deslocado por uma chave da KeyTable, conforme a posição (i &amp; 3).
/// </summary>
public static class PacketCipher
{
    /// <summary>Cifra o pacote no lugar, preenchendo KeyWord e CheckSum. Retorna o CheckSum.</summary>
    public static byte Encrypt(Span<byte> packet, KeyTable table, byte keyWord, ChecksumMode mode = ChecksumMode.CipherMinusPlain)
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

        byte checkSum = mode == ChecksumMode.CipherMinusPlain ? (byte)(cipherSum - plainSum) : (byte)(plainSum - cipherSum);
        packet[PacketHeader.CheckSumOffset] = checkSum;
        return checkSum;
    }

    /// <summary>Decifra o pacote no lugar. Retorna true se o CheckSum confere em qualquer uma das duas formas.</summary>
    public static bool Decrypt(Span<byte> packet, KeyTable table) => Decrypt(packet, table, out _);

    /// <summary>
    /// Decifra o pacote no lugar e diz qual forma de CheckSum o remetente usou
    /// (null se nenhuma confere, ou se as duas dão o mesmo valor e não dá para saber).
    /// </summary>
    public static bool Decrypt(Span<byte> packet, KeyTable table, out ChecksumMode? mode)
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

        bool cipherMinusPlain = expected == (byte)(cipherSum - plainSum);
        bool plainMinusCipher = expected == (byte)(plainSum - cipherSum);
        mode = cipherMinusPlain == plainMinusCipher ? null
             : cipherMinusPlain ? ChecksumMode.CipherMinusPlain
             : ChecksumMode.PlainMinusCipher;
        return cipherMinusPlain || plainMinusCipher;
    }
}
