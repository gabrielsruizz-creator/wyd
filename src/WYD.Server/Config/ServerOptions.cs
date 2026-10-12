namespace WYD.Server.Config;

/// <summary>Configurações do servidor (seção "Server" do appsettings.json).</summary>
public sealed class ServerOptions
{
    public const string Section = "Server";

    /// <summary>IP em que o servidor escuta. "0.0.0.0" aceita conexões de qualquer interface.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Porta do TMSrv. O client 7.54/7.56 conecta por padrão na 8281.</summary>
    public int Port { get; set; } = 8281;

    /// <summary>Versões de client aceitas no login (campo ClientVersion do pacote 0x20D). Vazio = 754 e 756.</summary>
    /// <remarks>Sem valor padrão no array: o binder de configuração acrescentaria os itens do appsettings a ele.</remarks>
    public int[] AllowedClientVersions { get; set; } = [];

    public IReadOnlyList<int> ClientVersions => AllowedClientVersions.Length > 0 ? AllowedClientVersions : [754, 756];

    /// <summary>Número máximo de jogadores conectados ao mesmo tempo.</summary>
    public int MaxUsers { get; set; } = 1000;

    /// <summary>Caminho do arquivo de 512 bytes com a tabela de chaves do client.</summary>
    public string KeyTablePath { get; set; } = "data/KeyTable.bin";

    /// <summary>Cria a conta automaticamente no primeiro login (útil para testes locais).</summary>
    public bool AutoRegister { get; set; }

    /// <summary>Desconecta clientes sem enviar nada por este tempo.</summary>
    public int IdleTimeoutSeconds { get; set; } = 300;

    /// <summary>Registra o conteúdo (hex) de todos os pacotes. Use para validar o protocolo com o client.</summary>
    public bool LogPackets { get; set; }

    /// <summary>Rejeita pacotes cujo checksum não confere. Deixe desligado até validar com o seu client.</summary>
    public bool StrictChecksum { get; set; }

    /// <summary>
    /// Porta HTTP das páginas de status dos canais (serv00.asp…), que o client consulta antes de conectar.
    /// 0 desliga. Se não for 80, o comando serverlist precisa incluir a porta no endereço.
    /// </summary>
    public int StatusPort { get; set; } = 80;

    /// <summary>Quantos números a página de status devolve (um por canal do grupo).</summary>
    public int StatusChannels { get; set; } = 11;

    /// <summary>Resposta fixa da página de status, para testes ("{online}" vira o número de jogadores; "\n" quebra linha).</summary>
    public string StatusBody { get; set; } = "";

    /// <summary>Posição inicial dos personagens novos (Armia).</summary>
    public int StartX { get; set; } = 2100;
    public int StartY { get; set; } = 2100;
}

/// <summary>Configuração do banco (seção "Database").</summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public string ConnectionString { get; set; } =
        "Server=127.0.0.1;Port=3306;Database=wyd;User ID=wyd;Password=wyd;";
}
