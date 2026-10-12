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

```powershell
# 1. Banco de dados (pule se você já tem um MySQL/MariaDB)
docker compose up -d

# 2. Crie uma conta
dotnet run --project src\WYD.Server -- create-account minhaconta minhasenha
```

As tabelas são criadas sozinhas na primeira execução (o script fica em [`sql/schema.sql`](sql/schema.sql)).

## Conectar o client

Use um client da versão **7.54 ou 7.56**. Feche o jogo antes de começar.

**1. Aponte o client para o seu PC** (troca os IPs do `serverlist.bin` e guarda um backup `.bak`):

```powershell
dotnet run --project src\WYD.Server -- serverlist "C:\caminho\do\jogo\serverlist.bin" 127.0.0.1
```

Isso troca os IPs dos canais e também as páginas de status (`http://www.wyd.com.br/.../serv01.asp`),
que o client consulta para saber se cada canal existe: sem resposta, todos aparecem "Inexistente." e
ele se recusa a conectar. O próprio servidor responde essas páginas na porta **80**.

Rode sem o IP no final para só ver o que tem no arquivo. Para amigos jogarem de outros PCs, use o seu
IP público e libere as portas **8281** e **80** no firewall e no roteador.

**2. Ache a KeyTable** (veja [`data/README.md`](data/README.md)):

```powershell
dotnet run --project src\WYD.Server -- find-keytable "C:\caminho\do\jogo\WYD.exe"
```

Com o comando esperando, abra o jogo, escolha o servidor e tente logar com a conta que você criou.
O jogo vai dar erro de conexão; isso é normal nesta etapa. Se o comando pedir, tente logar de novo.

**3. Inicie o servidor e entre no jogo:**

```powershell
dotnet run --project src\WYD.Server -- --Server:LogPackets=true
```

Com `LogPackets` ligado, o console mostra cada pacote trocado com o client, o que ajuda a corrigir
qualquer diferença de protocolo.

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
| `Server:StatusPort`            | `80`                | Porta das páginas de status dos canais (0 desliga)          |
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
