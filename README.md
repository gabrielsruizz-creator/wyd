# Servidor WYD (With Your Destiny) 7.57

Emulador de servidor de **With Your Destiny** escrito do zero em **C# / .NET 8** com **MySQL/MariaDB**,
compatível com o protocolo do client 7.57 (o 7.55 com as atualizações oficiais até a 7580). O suporte ao 7.54
ficou no histórico do git.

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
- [x] Comandos de GM no chat (`/gm item`, `/gm nivel`, `/gm gold`, `/gm ir`, `/gm trazer`…) e sussurro `/nome texto`
- [x] Combate (1ª etapa): atacar monstros, dano, morte, experiência e renascimento
- [x] Itens: equipar/mover (conferindo slot e nível), usar barras de gold e poções, comprar e vender nas lojas
- [x] Combate (2ª etapa): gold dos monstros, subir de nível (com a tabela de experiência do client),
      monstros revidam e perseguem, morte e renascer na cidade, vida e mana voltando com o tempo
- [x] Evoluções: Mortal (até o 400) → Arch (até o 400) → Celestial (até o 200) → Sub Celestial (até o 200),
      feita no nível máximo com o rei do seu reino
- [x] Reinos pela capa: Azul (Rei Harabard), Vermelho (Rei Glantuar) e Neutro (capas brancas), `/kingdom` e `/king`
- [x] Drop dos monstros direto para o inventário de quem matou (junto com o gold): cada item da lista do NPC tem
      `Server:DropChance` (200 = 2%) de cair, multiplicado pelo bônus de drop (Garnet, ancient de drop). Como no
      7.54, não há item no chão (largar no chão é ignorado)
- [x] Refinação com Pó de Oriharukon (412, até +6) e Pó de Laktorerium (413, até +9), com as chances do client
      (pelo grau do item, efeito 87); cada falha aumenta a chance da próxima tentativa. O Laktorerium também tenta o
      +10 → +11 (3%), e o Laktorerium 100 (4141) passa o +10 direto para +11
- [x] Gemas (3386 a 3389): Diamante (experiência), Esmeralda (absorção), Coral (perfuração), Garnet (drop), cada item
      equipado com gema vale 10%. No ancient troca a versão do item; em qualquer item +10 ou mais (arma ou armadura)
      fica nos bits da refinação e muda o brilho
- [x] Pintura: Tintura (3397 a 3406) + Pó de Oriharukon = Feijão Mágico (3407 a 3416), que arrastado no elmo, peitoral,
      calça, luva, bota ou escudo troca a cor do brilho da refinação (10 cores; aparece a partir do +1). O Removedor de
      Tintura (3417) volta à cor normal. A refinação continua a mesma
- [x] Mestres de skill (Cap.Cavaleiros, Foema_Ancian, Mestre_Archi, ForeLearner): lista das skills e aprender,
      gastando os pontos de skill da `SkillData.bin`
- [x] Sefirot no mestre de skill: Eternal Stone no slot 10 + 8 pedras + 30 milhões de gold
- [x] Eternal Stone no Oracle do reino (BlueOracle no castelo azul, RedOracle no vermelho; neutros em qualquer um):
      Unicorn's Spirit + Phoenix's Spirit + 10 Sapphires
- [x] Usar skills: dano físico, dano mágico e cura, gastando mana
- [x] Buffs das skills (em si ou em aliado), com ícone e tempo no client: dano (Assault, Power Up, Magic Weapon, Growth,
      Weapon Mastery), defesa (Magic Shield, Transfer Armor), magia (Skill Amplify), velocidade (Haste), esquiva
      (Dodge), Controle de Mana (30% da mana máxima vira vida), Aura da Vida e as auras de dano (Tempestade de Raios,
      Corrente Elemental, que batem nos monstros em volta a cada 2 s); os outros mostram o ícone, mas ainda não têm
      efeito. Precisa da `data/SkillData.bin`
- [x] Teleportes das cidades (0x290), com destino e preço montados pelo GM no jogo (`/gm tp add`)
- [x] FOR, DES, CON, INT e especiais contam na ficha (dano, vida, mana) como o client calcula; acessórios com HP%,
      MP% e regeneração funcionam
- [x] Equipamento soma dano, defesa, HP, MP, atributos e especiais, com refinação (+1 a +15) como o client
- [x] Velocidade de corrida com botas (efeito 29) e montarias (vida e nível guardados no item), até 6
- [x] Distribuir pontos de atributo (FOR, INT, DES, CON) e especiais pelo "+" da ficha
- [x] Banco MySQL/MariaDB com criação automática das tabelas
- [x] Log de pacotes em hexadecimal para validar o protocolo com o client
- [x] Testes automáticos (cifra, montagem de pacotes e fluxo completo por TCP)

## Próximas etapas

1. Drops: itens dos monstros caindo no chão e sendo pegos.
2. Buffs das skills, baú e NPCs de quest.
3. Grupo, troca, guild.
4. Teleportes, quests.

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

Personagem preso num lugar que derruba o client: `dotnet run --project src\WYD.Server -- unstuck minhaconta`
(leva os personagens da conta para a cidade inicial; o jogador precisa estar fora do jogo). O servidor também
desfaz sozinho um teleporte quando o client cai logo depois dele, antes de o jogador andar.

A segunda senha (a numérica da tela de personagens) é definida no primeiro login: a primeira senha
digitada passa a ser a da conta. Para apagá-la: `dotnet run --project src\WYD.Server -- reset-second-password minhaconta`.

As tabelas são criadas sozinhas na primeira execução (o script fica em [`sql/schema.sql`](sql/schema.sql)).

## Conectar o client

Use o client **7.57**: o 7.55 com as atualizações aplicadas em ordem, de `75507551.zip` até `75747580.zip`
(cada uma extraída por cima, substituindo os arquivos). Feche o jogo antes de começar.

**1. Aponte o client para o seu PC** (troca os IPs do `serverlist.bin` e guarda um backup `.bak`):

```powershell
dotnet run --project src\WYD.Server -- serverlist "C:\caminho\do\jogo\serverlist.bin" 127.0.0.1
```

Isso troca os IPs dos canais e também as páginas de status (`http://www.wyd.com.br/.../serv01.asp`),
que o client consulta para saber se cada canal existe: sem resposta, todos aparecem "Inexistente." e
ele se recusa a conectar. O próprio servidor responde essas páginas na porta **80**.

Rode sem o IP no final para só ver o que tem no arquivo. Para amigos jogarem de outros PCs, use o seu
IP público e libere as portas **8281** e **80** no firewall e no roteador.

Aponte também o launcher (`@WYDLauncher.exe`) para o servidor. Ele busca a lista de atualização no endereço da
primeira linha do `Settings.txt`; com o endereço oficial, que não existe mais, ele para em "Verificando a
atualização" e não libera o jogo nem as Configurações (modo janela, resolução, som):

```powershell
dotnet run --project src\WYD.Server -- launcher "C:\caminho\do\jogo" 127.0.0.1
```

Com o servidor aberto, o launcher passa direto. Sem o launcher, o modo de tela e a resolução (o `Config.bin`, que
o WYD.exe 7.57 lê no lugar do `config.txt`) também podem ser trocados por aqui:

```powershell
dotnet run --project src\WYD.Server -- client-config "C:\caminho\do\jogo" janela 1024x768
```

**2. Ache a KeyTable** (veja [`data/README.md`](data/README.md)):

```powershell
dotnet run --project src\WYD.Server -- find-keytable "C:\caminho\do\jogo\WYD.exe"
```

Com o comando esperando, abra o jogo, escolha o servidor e tente logar com a conta que você criou.
O jogo vai dar erro de conexão; isso é normal nesta etapa. Se o comando pedir, tente logar de novo.

**3. Copie as tabelas de experiência** (Mortal/Arch e Celestial; sem elas os personagens não sobem de nível):

```powershell
dotnet run --project src\WYD.Server -- extract-exptable "C:\caminho\do\jogo\WYD.exe"
```

**4. Acerte a ItemList e a SkillData** (as do patch 7580 não batem com o checksum que o WYD.exe dele espera, e o
jogo pararia com "Initialize Data Failed"). O comando acerta as duas no client e copia para `data/`:

```powershell
dotnet run --project src\WYD.Server -- fix-lists "C:\caminho\do\jogo"
```

Na mesma pasta, devolva o brilho de refinação e de ancient do Hrotti (a textura dele veio marcada errada na
`mesh\MeshTextureList.bin` original; a lista original fica em `MeshTextureList.bin.bak`):

```powershell
dotnet run --project src\WYD.Server -- fix-mesh-textures "C:\caminho\do\jogo"
```

**5. Corrija o client** (opcional; funciona no WYD.exe 7.57 e no 7.54; no 7.57 só a correção das armas, porque a
ItemList dele já põe cada item em 1 slot): o efeito das armas +13 a +15 (sem isso a arma +13 ou mais solta o efeito do
Grade seguinte, errado; o nome e a refinação continuam os de verdade, só o efeito para no do +12) e o inventário
com cada item ocupando 1 slot (no original as botas ocupam 2×2, as armas até 2×4).

No seu PC, direto no arquivo (o original fica em `WYD.exe.bak`):

```powershell
dotnet run --project src\WYD.Server -- patch-client "C:\caminho\do\jogo\WYD.exe"
```

Para os jogadores receberem pelo WYDLauncher:

```powershell
dotnet run --project src\WYD.Server -- publish-client "C:\caminho\do\jogo\WYD.exe"
```

Isso põe o WYD.EXE corrigido e o `update1.htm` em `data/patch` (fora do repositório). Com o servidor aberto, o
launcher baixa pela porta 80, a mesma do status dos canais. Em cada client, troque a **primeira linha** do
`Settings.txt` (na pasta do jogo) pelo endereço do servidor, terminado em `/`:

```
http://127.0.0.1/
```

(o IP do servidor para quem joga de outro PC; o launcher não aceita outra porta). Ao abrir, o launcher baixa o
WYD.EXE uma vez e o `[VERSION]` do `config.txt` sobe para a versão publicada. Cada `publish-client` cria uma versão
nova, então rode de novo quando trocar o arquivo.

**6. Inicie o servidor e entre no jogo:**

```powershell
dotnet run --project src\WYD.Server -- --Server:LogPackets=true
```

Com `LogPackets` ligado, o console mostra cada pacote trocado com o client, o que ajuda a corrigir
qualquer diferença de protocolo.

## Contas

```powershell
dotnet run --project src\WYD.Server -- accounts                       # todas as contas e os personagens
dotnet run --project src\WYD.Server -- set-password <conta> <senha>   # troca a senha (para entrar numa conta)
dotnet run --project src\WYD.Server -- show <conta> <personagem>      # ficha, equipamento e inventário
```

As senhas ficam gravadas só como hash: não dá para ver a senha de uma conta, só trocar.

## Comandos de GM

Dê nível de GM a uma conta (vale a partir do próximo login):

```powershell
dotnet run --project src\WYD.Server -- set-access minhaconta 1
```

No jogo, digite no chat (todos os comandos de GM começam com `/gm`):

| Comando | O que faz |
|---------|-----------|
| `/gm item 905` | cria o item 905 no primeiro espaço livre do inventário |
| `/gm item 2370` | montarias (2360 a 2389, 3980 a 3998) já vêm vivas: vida 100, nível 1 |
| `/gm item 905 43 9` | o mesmo, com até 3 pares efeito/valor (ex.: 43 = refinação) |
| `/gm nivel 100` | coloca o seu personagem no nível 100 (vida, mana e pontos de acordo) |
| `/gm evoluir arch` | muda a sua evolução: `mortal`, `arch`, `celestial` ou `sub` (volta ao nível 1) |
| `/gm pontos 100` | dá 100 pontos de atributo, especiais e de skill |
| `/gm gold 1000000` | define o seu gold (até 2.000.000.000) |
| `/gm curar` | enche a sua vida e mana |
| `/gm ir 2100 2100` | teleporta você para a posição X Y |
| `/gm ir Fulano` | teleporta você até o jogador |
| `/gm trazer Fulano` | traz o jogador até você |
| `/gm pos` | mostra a sua posição (X Y) |
| `/gm reino azul` | o `/kingdom` do reino (`azul`, `vermelho` ou `neutro`) passa a levar para onde você está |
| `/gm tp add 2480 1700 500 Azran` | o círculo de teleporte onde você está passa a levar para 2480 1700, por 500 de gold |
| | já vêm prontos: Armia (2117 2102) → Noatun (1045 1725) e Noatun (1046 1718) → Azran (2482 1718), 700 de gold cada |
| `/gm tp remover` | apaga o teleporte onde você está |
| `/gm tp lista` | mostra os teleportes perto de você |
| `/gm online` | lista quem está no jogo, com nível e posição |
| `/gm aviso texto` | mensagem para todos os jogadores |
| `/gm kick Fulano` | desconecta o jogador |
| `/gm face <índice>` | troca o rosto, o modelo do personagem (1 TK, 11 FM, 21 BM, 31 HT ou o modelo de um monstro) |
| `/gm capa <índice>` | troca a capa equipada direto no slot dela (0 tira a capa; as capas não se equipam arrastando) |
| `/gm limpar` | esvazia o seu inventário (os 64 slots) e redesenha todos os slots no client |
| `/gm soul` | dá a Soul (Limite da Alma) ao seu personagem Mortal |
| `/gm soul <1-10>` | escolhe a combinação da Soul |
| `/gm inventario` | lista os itens que o servidor tem no seu inventário, com linha e coluna |
| `/gm npc` | lista os NPCs e monstros perto de você (id, nome, posição) |
| `/gm npc remover <id\|nome>` | tira o NPC do mapa até reiniciar o servidor (para achar NPC que derruba o client) |
| `/gm ajuda` | lista os comandos de GM no chat |

Para todos os jogadores:

| Comando | O que faz |
|---------|-----------|
| `/ajuda` | lista os comandos |
| `/add for 100` | distribui 100 pontos em FOR (`for`, `int`, `des`, `con`, `esp1` a `esp4`); vários de uma vez: `/add for 100 con 50` |
| `/kingdom` ou `/king` | vai para o castelo do seu reino (neutros vão para o centro do reino) |

**Evolução:** no nível máximo (Mortal 400 → Arch, Arch 400 → Celestial, Celestial 200 → Sub Celestial), fale
com o rei do seu reino. O reino é a capa: azul (Hekalotia) evolui com o Rei Harabard, vermelha (Akeronia) com o
Rei Glantuar, e branca (ou sem capa) com qualquer um dos dois.

**Arch:** o Mortal 400 equipa a Eternal Stone (slot 10) e o Sefirot da classe que o Arch vai ter (slot 11, ao lado)
e fala com o rei; sem os dois equipados o client nem manda o clique. O Sefirot é gasto e a Eternal Stone fica. Nasce um personagem novo, com o mesmo
nome, num slot livre da conta: corpo da classe do Mortal, skills da classe do Sefirot (ex.: corpo de HT com skills
de Foema), nível 1, só com a Cythera de Prata (3500) no elmo e com 528 pontos de atributo. O Mortal continua na conta e o client volta para a
seleção de personagens. Do Arch em diante o nível mínimo dos equipamentos não vale.
| `/Fulano oi` | sussurra "oi" para o jogador Fulano |

Para achar o número de um item: `dotnet run --project src\WYD.Server -- items data\ItemList.bin Espada`.
Com o `ItemList.bin` em `data/`, o servidor recusa índices que não existem.

## Configuração

Tudo fica em [`src/WYD.Server/appsettings.json`](src/WYD.Server/appsettings.json). Também dá para
sobrescrever qualquer opção pela linha de comando, por exemplo `--Server:LogPackets=true`.

| Opção                          | Padrão              | Para que serve                                              |
|--------------------------------|---------------------|-------------------------------------------------------------|
| `Server:Port`                  | `8281`              | Porta em que o client conecta                               |
| `Server:AllowedClientVersions` | `[758]`             | Versões de client aceitas                                   |
| `Server:KeyTablePath`          | `data/KeyTable.bin` | Tabela de cifra do client                                   |
| `Server:AutoRegister`          | `false`             | Cria a conta no primeiro login (bom para testes)            |
| `Server:LogPackets`            | `false`             | Mostra todos os pacotes em hex no console                   |
| `Server:StrictChecksum`        | `false`             | Derruba quem mandar pacote com checksum errado              |
| `Server:StatusPort`            | `80`                | Porta das páginas de status dos canais (0 desliga)          |
| `Server:StartX` / `StartY`     | `2100` / `2100`     | Onde os personagens novos começam                           |
| `Server:DropChance`            | `200`               | Chance de cada item da lista do monstro cair, em 1/10000    |
| `Server:PatchPath`             | `data/patch`        | Arquivos que o WYDLauncher baixa (`publish-client`)         |
| `Server:ExpTablePath`          | `data/ExpTable.txt` | Experiência de cada nível (`extract-exptable`)              |
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
