# Servidor WYD (With Your Destiny) 7.54 / 7.56

Emulador de servidor de **With Your Destiny** escrito do zero em **C# / .NET 8** com **MySQL/MariaDB**,
compatível com o protocolo do client das versões 7.54 e 7.56.

> Este projeto não contém nem usa código ou arquivos do jogo original. Os arquivos do client
> (como a `KeyTable.bin`) você precisa fornecer a partir da sua própria cópia; veja [`data/README.md`](data/README.md).

## O que já funciona

- [x] Servidor TCP na porta 8281 com várias conexões simultâneas
- [x] Handshake (INITCODE) e cifra/decifra dos pacotes com a KeyTable do client
- [x] Login de conta com senha em hash (PBKDF2), conferência da versão do client, contas bloqueadas
      e derrubada da sessão antiga quando a mesma conta loga em outro lugar
- [x] Tela de seleção: lista de personagens e baú da conta
- [x] Criar personagem (TK, FM, BM, HT) e apagar personagem (com confirmação de senha)
- [x] Banco MySQL/MariaDB com criação automática das tabelas
- [x] Log de pacotes em hexadecimal para validar o protocolo com o client
- [x] Testes automáticos (cifra, montagem de pacotes e fluxo completo por TCP)

## Próximas etapas

1. **Validar com o client real**: KeyTable e os layouts marcados ⚠️ em [`docs/protocolo.md`](docs/protocolo.md).
2. **Entrar no mundo**: pacote de entrada com o `STRUCT_MOB` completo, mapa e posição.
3. Movimento, visão de outros jogadores e chat.
4. Leitura dos arquivos de dados (lista de itens, NPCs, mobs).
5. Mobs, combate, skills, drops, NPCs de loja, baú, grupo, guild, quests…

## Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- MySQL 8 ou MariaDB 10.6+ (ou Docker, com o `docker-compose.yml` incluso)
- A `KeyTable.bin` da sua versão do client em `data/` (veja [`data/README.md`](data/README.md))

## Como rodar

```bash
# 1. Banco de dados (pule se você já tem um MySQL/MariaDB)
docker compose up -d

# 2. Coloque o arquivo data/KeyTable.bin (veja data/README.md)

# 3. Crie uma conta
dotnet run --project src/WYD.Server -- create-account minhaconta minhasenha

# 4. Inicie o servidor
dotnet run --project src/WYD.Server
```

As tabelas são criadas sozinhas na primeira execução (o script fica em [`sql/schema.sql`](sql/schema.sql)).

Para apontar o client para o seu servidor, edite a lista de servidores do client (`serverlist`)
com o IP da máquina onde o servidor está rodando, e libere a porta **8281** no firewall/roteador.

## Configuração

Tudo fica em [`src/WYD.Server/appsettings.json`](src/WYD.Server/appsettings.json). Também dá para
sobrescrever qualquer opção pela linha de comando, por exemplo `--Server:LogPackets=true`.

| Opção                          | Padrão              | Para que serve                                              |
|--------------------------------|---------------------|-------------------------------------------------------------|
| `Server:Port`                  | `8281`              | Porta em que o client conecta                               |
| `Server:AllowedClientVersions` | `[754, 756]`        | Versões de client aceitas                                   |
| `Server:KeyTablePath`          | `data/KeyTable.bin` | Tabela de cifra do client                                   |
| `Server:AutoRegister`          | `false`             | Cria a conta no primeiro login (bom para testes)            |
| `Server:LogPackets`            | `false`             | Mostra todos os pacotes em hex no console                   |
| `Server:StrictChecksum`        | `false`             | Derruba quem mandar pacote com checksum errado              |
| `Server:StartX` / `StartY`     | `2100` / `2100`     | Onde os personagens novos começam                           |
| `Database:ConnectionString`    | `…Database=wyd…`    | Conexão com o MySQL                                         |

## Testes

```bash
dotnet test
```

Os testes sobem o servidor de verdade numa porta aleatória, com um banco em memória, e conversam
com ele como um client (login, criar e apagar personagem, senha errada, versão errada, login duplicado).

## Estrutura

```
src/WYD.Server/
  Network/    TCP, sessões, cifra (PacketCipher), leitura/escrita de pacotes
  Protocol/   opcodes, structs do jogo (ITEM, SCORE, SELCHAR) e layouts dos pacotes
  Handlers/   tratamento de cada pacote (login, personagens)
  Database/   acesso ao MySQL
  Game/       modelos (conta, personagem) e atributos iniciais das classes
  Security/   hash de senha
sql/          esquema do banco
docs/         documentação do protocolo
tests/        testes automáticos
```
