using System.Buffers.Binary;

namespace WYD.Server.Network;

public sealed class ProtocolException(string message) : Exception(message);

/// <summary>
/// Separa o fluxo TCP em pacotes completos. Na primeira leitura exige o INITCODE de 4 bytes
/// que o client envia ao conectar (às vezes junto com o primeiro pacote).
/// </summary>
public sealed class PacketFramer
{
    private byte[] _buffer = new byte[PacketHeader.MaxPacketSize * 2];
    private int _length;
    private bool _initReceived;

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_length + data.Length > _buffer.Length)
        {
            if (_length + data.Length > PacketHeader.MaxPacketSize * 8)
                throw new ProtocolException("Buffer de recepção excedido.");
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + data.Length));
        }
        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }

    /// <summary>Retorna o próximo pacote completo (ainda cifrado), ou null se faltam bytes.</summary>
    public byte[]? TryRead()
    {
        if (!_initReceived)
        {
            if (_length < 4) return null;
            uint init = BinaryPrimitives.ReadUInt32LittleEndian(_buffer);
            if (init != PacketHeader.InitCode)
                throw new ProtocolException($"INITCODE inválido: 0x{init:X8}.");
            Consume(4);
            _initReceived = true;
        }

        if (_length < 2) return null;
        int size = PacketHeader.ReadSize(_buffer);
        if (size < PacketHeader.Size || size > PacketHeader.MaxPacketSize)
            throw new ProtocolException($"Tamanho de pacote inválido: {size}.");
        if (_length < size) return null;

        var packet = _buffer.AsSpan(0, size).ToArray();
        Consume(size);
        return packet;
    }

    private void Consume(int count)
    {
        _length -= count;
        if (_length > 0)
            Buffer.BlockCopy(_buffer, count, _buffer, 0, _length);
    }
}
