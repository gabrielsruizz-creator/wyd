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
- [x] Segunda senha (numérica) com troca de senha e bloqueio após 5 erros
- [x] Criar personagem (TK, FM, BM, HT) e apagar personagem (com confirmação de senha)
- [x] Entrar no mundo com o personagem escolhido (pacote `0x114` com a ficha completa)
- [x] Ver os outros jogadores, andar (a posição fica salva) e chat local
- [x] Monstros e NPCs do `NPCGener.txt` original no mapa, andando pela área de cada spawn
- [x] Lojas dos NPCs abrem com os itens de cada um
- [x] Comandos no chat: `/item` para GMs e sussurro `/nome texto`
- [x] Combate (1ª etapa): atacar monstros, dano, morte, experiência e renascimento
- [x] Banco MySQL/MariaDB com criação automática das tabelas
- [x] Log de pacotes em hexadecimal para validar o protocolo com o client
- [x] Testes automáticos (cifra, montagem de pacotes e fluxo completo por TCP)

## Próximas etapas

1. Combate (2ª etapa): monstros revidam, subir de nível, drops e gold.
2. Comprar e vender nas lojas, baú, mestres de skill e NPCs de quest, skills, drops, NPCs de loja, baú, grupo, guild, quests…

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

A segunda senha (a numérica da tela de personagens) é definida no primeiro login: a primeira senha
digitada passa a ser a da conta. Para apagá-la: `dotnet run --project src\WYD.Server -- reset-second-password minhaconta`.

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

## Comandos de GM

Dê nível de GM a uma conta (vale a partir do próximo login):

```powershell
dotnet run --project src\WYD.Server -- set-access minhaconta 1
```

No jogo, digite no chat:

| Comando | O que faz |
|---------|-----------|
| `/item 905` | cria o item 905 no primeiro espaço livre do inventário |
| `/item 905 43 9` | o mesmo, com até 3 pares efeito/valor (ex.: 43 = refinação) |
| `/ajuda` | lista os comandos |
| `/Fulano oi` | sussurra "oi" para o jogador Fulano (qualquer conta) |

Para achar o número de um item: `dotnet run --project src\WYD.Server -- items data\ItemList.bin Espada`.
Com o `ItemList.bin` em `data/`, o servidor recusa índices que não existem.

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
