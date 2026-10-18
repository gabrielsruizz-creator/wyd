using System.Buffers.Binary;
using System.Text;

namespace WYD.Server.Tools;

/// <summary>
/// Configurações do client 7.57 que ficam fora do jogo.
/// <list type="bullet">
/// <item>Settings.txt: a primeira linha é o endereço de onde o launcher (@WYDLauncher.exe) baixa a lista de
/// atualização (endereço + update1.htm). Com o endereço oficial, que não existe mais, ele fica parado em
/// "Verificando a atualização" e não libera o jogo nem as Configurações.</item>
/// <item>Config.bin: 15 shorts que o WYD.exe lê ao abrir (0x549A48; sem o arquivo ele usa os valores de
/// <see cref="Defaults"/>) e que o launcher grava pela tela de Configurações, na ordem do config.txt antigo:
/// VERSION, RES, ANIMATION, SOUND, MUSIC, SERVER, BRIGHT, CURSOR, DEMO, WINDOW, CLASSIC, CAMERAROTATE, DXT,
/// KEYTYPE, CAMERAVIEW. WINDOW 0 = tela cheia. RES vai de 1 a 11 (<see cref="Resolutions"/>).</item>
/// </list>
/// </summary>
public static class ClientSettings
{
    public const int ConfigSize = 30;
    public const int Version = 0, Resolution = 1, Window = 9;

    public static readonly string[] Names =
        ["VERSION", "RES", "ANIMATION", "SOUND", "MUSIC", "SERVER", "BRIGHT", "CURSOR", "DEMO", "WINDOW", "CLASSIC",
         "CAMERAROTATE", "DXT", "KEYTYPE", "CAMERAVIEW"];

    /// <summary>Os valores que o WYD.exe usa sem o Config.bin.</summary>
    public static readonly short[] Defaults = [7000, 7, 2, 100, 100, -1, 57, 2, 1, 1, 1, 0, 0, 0, 1];

    /// <summary>Tabela de resoluções do WYD.exe 7.57 (RES 1 a 11): largura, altura, bits de cor.</summary>
    public static readonly (int Width, int Height, int Bits)[] Resolutions =
    [
        (640, 480, 16), (800, 600, 16), (1024, 768, 16), (1280, 1024, 16), (1600, 1200, 16),
        (640, 480, 32), (800, 600, 32), (1024, 768, 32), (1280, 1024, 32), (1600, 1200, 32), (3200, 2400, 32),
    ];

    public static short[] ReadConfig(byte[]? file)
    {
        if (file is null || file.Length < ConfigSize) return (short[])Defaults.Clone();
        var values = new short[Names.Length];
        for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadInt16LittleEndian(file.AsSpan(i * 2));
        return values;
    }

    public static byte[] WriteConfig(short[] values)
    {
        var file = new byte[ConfigSize];
        for (int i = 0; i < Names.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(i * 2), values[i]);
        return file;
    }

    /// <summary>RES de "1024x768" (32 bits) ou do número direto; null se não existir.</summary>
    public static short? ParseResolution(string text)
    {
        if (short.TryParse(text, out short n)) return n is >= 1 and <= 11 ? n : null;
        var parts = text.ToLowerInvariant().Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h)) return null;
        int i = Array.FindLastIndex(Resolutions, r => r.Width == w && r.Height == h);
        return i < 0 ? null : (short)(i + 1);
    }

    public static string Describe(short res) =>
        res is >= 1 and <= 11 ? $"{Resolutions[res - 1].Width}x{Resolutions[res - 1].Height} ({Resolutions[res - 1].Bits} bits)" : $"{res} (inválida)";

    /// <summary>Troca a primeira linha do Settings.txt pelo endereço do servidor, mantendo o resto e as quebras de linha.</summary>
    public static byte[] SetPatchAddress(byte[] settings, string address)
    {
        string text = Encoding.Latin1.GetString(settings);
        int end = text.IndexOf('\n');
        string rest = end < 0 ? "" : text[end..];
        bool crlf = end > 0 && text[end - 1] == '\r';
        if (crlf) rest = "\r" + rest;
        return Encoding.Latin1.GetBytes(address + rest);
    }

    public static string PatchAddress(byte[] settings)
    {
        string text = Encoding.Latin1.GetString(settings);
        int end = text.IndexOf('\n');
        return (end < 0 ? text : text[..end]).TrimEnd('\r');
    }
}
