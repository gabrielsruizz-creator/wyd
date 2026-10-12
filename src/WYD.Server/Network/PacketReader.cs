using System.Buffers.Binary;
using System.Text;

namespace WYD.Server.Network;

/// <summary>Lê os campos de um pacote já decifrado, começando logo após o cabeçalho.</summary>
public sealed class PacketReader
{
    private readonly byte[] _buffer;
    private int _position;

    public PacketReader(byte[] packet)
    {
        _buffer = packet;
        _position = PacketHeader.Size;
    }

    public int Length => _buffer.Length;
    public int Remaining => _buffer.Length - _position;
    public ushort Type => PacketHeader.ReadType(_buffer);
    public ushort Id => PacketHeader.ReadId(_buffer);

    /// <summary>Cópia do pacote inteiro (já decifrado), para repassar a outros jogadores.</summary>
    public byte[] ToArray() => (byte[])_buffer.Clone();

    public byte ReadByte() => _buffer[_position++];

    public short ReadInt16()
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(_buffer.AsSpan(_position));
        _position += 2;
        return value;
    }

    public ushort ReadUInt16()
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_position));
        _position += 2;
        return value;
    }

    public int ReadInt32()
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(_position));
        _position += 4;
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        var span = _buffer.AsSpan(_position, count);
        _position += count;
        return span;
    }

    /// <summary>Lê um char[length] terminado em zero.</summary>
    public string ReadString(int length)
    {
        var field = ReadBytes(length);
        int end = field.IndexOf((byte)0);
        if (end >= 0) field = field[..end];
        return Encoding.Latin1.GetString(field);
    }

    public void Skip(int count) => _position += count;
}
