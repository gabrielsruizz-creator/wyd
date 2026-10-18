# Protocolo do client 7.57

Como o servidor conversa com o client. Tudo é little-endian, e as strings são `char[N]`
de tamanho fixo terminadas em zero.

O servidor fala o protocolo do **client 7.57** (base 7.55 com os patches até o 7580). A comparação com o 7.54, com os
endereços de cada leitura no WYD.exe, está em [`cliente-757.md`](cliente-757.md). As seções mais abaixo (mundo,
monstros, itens, skills...) foram escritas com o 7.54 e os endereços citados nelas são do exe 7.54; o comportamento
vale até ser conferido de novo no 7.57.

## Conexão

1. O client abre uma conexão TCP na porta **8281**.
2. Os 4 primeiros bytes enviados são o **INITCODE** `0x1F11F311` (às vezes no mesmo envio do
   primeiro pacote). Se não vierem, o servidor fecha a conexão.
3. Depois disso, o fluxo é uma sequência de pacotes, cada um começando pelo seu tamanho.

## Cabeçalho (12 bytes, `MSG_STANDARD`)

| Offset | Tipo     | Campo      | Observação                              |
|-------:|----------|------------|-----------------------------------------|
| 0      | `ushort` | `Size`     | tamanho total do pacote, em claro       |
| 2      | `byte`   | `KeyWord`  | semente da cifra, em claro              |
| 3      | `byte`   | `CheckSum` | soma de verificação, em claro           |
| 4      | `ushort` | `Type`     | opcode (cifrado daqui em diante)        |
| 6      | `ushort` | `ID`       | id da conexão / do mob                  |
| 8      | `uint`   | `Tick`     | relógio do client                       |

O `Tick` dos pacotes do servidor é o relógio do servidor em ms (começa em um dia). O client acerta o relógio dele pelo
`Tick` do `0x114` e do `0x10E` e compara com a hora em que cada skill foi usada pela última vez, que começa em 0 ao
entrar no mundo; se o `Tick` viesse perto de 0, as skills de Delay longo (a Soul tem 1200 s) nasceriam em recarga.

## Cifra

✅ Conferida com o client 7.54: a KeyTable encontrada pelo `find-keytable` decifra os pacotes do client.

Implementada em [`PacketCipher.cs`](../src/WYD.Server/Network/PacketCipher.cs). Usa a KeyTable de
512 bytes do client (ver [`data/README.md`](../data/README.md)):

```
pos = KeyTable[KeyWord * 2]
para i = 4 até Size-1:
    k = KeyTable[(pos % 256) * 2 + 1]
    i % 4 == 0:  byte[i] += k << 1      (decifrar: -=)
    i % 4 == 1:  byte[i] -= k >> 3      (decifrar: +=)
    i % 4 == 2:  byte[i] += k << 2      (decifrar: -=)
    i % 4 == 3:  byte[i] -= k >> 5      (decifrar: +=)
    pos++
CheckSum = soma(bytes cifrados) - soma(bytes em claro)   (mod 256)
```

O servidor sorteia um `KeyWord` novo para cada pacote que envia.

## Pacotes

**Situação:**
- ✅ conferido no client 7.54: pelo tráfego real ou pela leitura do código do `WYD.exe`;
- ⚠️ a validar.

### Client → Servidor

| Opcode  | Nome               | Tamanho | Conteúdo                                                                 | Situação |
|---------|--------------------|--------:|--------------------------------------------------------------------------|----------|
| `0x20D` | AccountLogin       | 116     | `Password[12]` (0x0C), `Account[16]` (0x18), 52 bytes, `int Version = 758` (0x5C), `int DBNeedSave`, `int Adapter[4]` | ✅ exe 7.57 |
| `0x20F` | CreateCharacter    | 36      | `int Slot`, `Name[16]`, `int Class`                                      | ✅ |
| `0x211` | DeleteCharacter    | 44      | `int Slot`, `Name[16]`, `Password[12]`                                   | ✅ |
| `0x213` | CharacterLogin     | 36      | `int Slot`, …                                                            | ✅ |
| `0xFDE` | SecondPassword     | 32      | `Password[16]`, `int Change` (0 = conferir, 1 = gravar nova)             | ✅ |
| `0x366` / `0x36C` | Movimento | 52      | `short X, Y`, `int Velocidade`, `int Tipo`, `short DestX, DestY` (0x18), `char Rota[24]` | ✅ |
| `0x369` | RequestMob         | 16      | `ushort Id` (0x0C): o client pede o 0x364 de um id que não conhece       | ✅ |
| `0x333` | Chat               | 108     | `char Texto[96]`                                                         | ✅ |
| `0x334` | Comando / sussurro | 128     | `char Cmd[16]` (0x0C) e o resto do texto (0x1C): "/item 905" chega como Cmd = "item", texto = "905" | ⚠️ layout lido no WYD.exe, falta ver no client |
| `0x39D` / `0x39E` / `0x367` | Ataque | 72 / 80 / 168 | `int Hold` (0x0C), `long Exp` (0x18), posições (0x22..0x28), `ushort Atacante` (0x2A), `byte Movimento` (0x2E), `int Mana` (0x34), `short Skill` (0x38), alvos `{int Id, int Dano = -1}` a partir de 0x3C e um int no fim | ✅ exe 7.57 |
| `0x277` | Distribuir ponto     | 20      | `short Tipo` (0 atributo, 1 especial, 2 skill no mestre), `short Qual` (0 FOR/1 INT/2 DES/3 CON ou especial 1-4), `short Extra` | ✅ |
| `0x378` | Barra de skills      | 32      | `byte Barra[20]`: skill dentro da classe (0-23), 0xFF vazio; volta no 0x114 (SkillBar[4] da STRUCT_MOB + 16 em 0x416) | ✅ layout |
| `0x28B` | Falar com NPC        | 20      | `int IdDoNpc`, `int Confirma` (reis, NPCs de quest) | ✅ layout |
| `0x290` | Teleporte          | 16      | só o cabeçalho e um int 0: o jogador confirmou o teleporte num círculo (célula com a flag 0x10 no mapa do client) | ✅ |
| `0x289` | Renascer            | 12      | só o cabeçalho: o personagem morto pede para renascer (botão, ou sozinho depois de 3 minutos) | ✅ |
| `0x376` | Mover/equipar item | 20      | `byte TipoDest, SlotDest, TipoOrig, SlotOrig` (0 equip, 1 inventário, 2 baú); o servidor devolve o pedido para o client fazer a troca | ✅ layout |
| `0x373` | Usar item          | 36      | `int TipoOrig, SlotOrig, TipoDest, SlotDest`                              | ✅ layout |
| `0x379` | Comprar            | 24      | `short Npc`, `short SlotDoNpc` (linha×9+coluna = slot do Carry), `short SlotMeu` | ✅ layout |
| `0x37A` | Vender             | 20      | `short Npc`, `short TipoOrig`, `short SlotOrig`                           | ✅ layout |
| `0x27B` | RequestShop        | 16      | `ushort IdDoNpc` (0x0C): o client só manda se `Score.Merchant & 0xF == 1` | ✅ |
| `0x3A0` | Ping               | 12      | ignorado                                                                 | ✅ |

### Servidor → Client

| Opcode  | Nome                | Tamanho | Conteúdo                                                                   | Situação |
|---------|---------------------|--------:|----------------------------------------------------------------------------|----------|
| `0x101` | MessagePanel        | 108     | `char Text[96]`; id do cabeçalho tem de ser 0 (com outro id o client ignora, 0x499438); faixa no alto por 4 s | ✅ |
| `0x10A` | AccountLoginOk      | 1928    | `Keys[16]` (0x0C), `SELCHAR` (0x020), `ITEM Cargo[128]` (0x368), `int Coin` (0x768), `Account[16]` (0x76C) | ✅ exe 7.57 |
| `0x364` | CreateMob           | 232     | põe um jogador na tela (layout abaixo)                                     | ⚠️ alinhado com o 7.54 |
| `0x39D` / `0x39E` / `0x367` | Ataque | 72 / 80 / 168 | o mesmo pacote com o dano de cada alvo; `int Hold` (0x0C) e `long ExpDoAtacante` (0x18) atualizam a barra de experiência | ✅ exe 7.57 |
| `0x338` | MobKill             | 32      | `int Hold`, `ushort Morto` (0x10), `ushort Matador` (0x12), `long ExpDoMatador` (0x18) | ✅ exe 7.57 |
| `0x379` | BuyOk               | 24      | o pedido com o slot final e `int GoldTotal` (0x14), que o client copia para o gold | ✅ layout |
| `0x3AF` | UpdateCoin          | 16      | `int Gold`                                                                 | ✅ layout |
| `0x181` | UpdateHpMp          | 28      | `int Hp`, `int Mp`, `int HpPedido`, `int MpPedido`                         | ✅ exe 7.57 |
| `0x3B9` | AffectList          | 140     | `AFFECT[16]` {byte Tipo, byte Valor, short Nível, int Tempo em 8 s}: ícones dos buffs no canto | ✅ layout |
| `0x36B` | UpdateEquip         | 60      | `ushort Equip[16]`, `byte Refino[16]` (para quem está vendo)               | ⚠️ |
| `0x337` | UpdateEtc           | 48      | `int Hold`, `long Exp` (0x10), `uint LearnedSkill[2]` (0x18), `short ScoreBonus, SpecialBonus, SkillBonus` (0x20), `int Gold` (0x28) | ✅ exe 7.57 |
| `0x336` | UpdateScore         | 144     | `SCORE` (48) @0x0C, Critical/SaveMana @0x3C, `short Affect[32]` @0x3E, Guild @0x7E, GuildLevel @0x80 | ✅ exe 7.57 |
| `0x182` | UpdateItem          | 24      | `short Tipo` (0 equip, 1 inventário, 2 baú), `short Slot`, `ITEM`          | ✅ |
| `0x334` | Sussurro            | 128     | `char De[16]`, texto                                                       | ⚠️ |
| `0x17C` | ShopList            | 236     | `int Tipo` (1 = itens), `ITEM Lista[27]`, `int Imposto`; aba *n* vem do `Carry[n*27 .. n*27+8]` do NPC | ✅ |
| `0x165` | RemoveMob           | 16      | id no cabeçalho, `int Tipo` (0x0C)                                         | ✅ |
| `0x366` | Move                | 52      | o mesmo pacote do client, com o id de quem andou no cabeçalho; `int Tipo` (0x14) = 1 teleporta direto para (DestX, DestY) | ✅ |
| `0x333` | Chat                | 108     | o mesmo pacote do client, com o id de quem falou (não volta para ele)      | ✅ |
| `0x110` | CreateCharacterOk   | 856     | `int` (0x0C, não lido), `SELCHAR` (0x10)                                   | ✅ exe 7.57 |
| `0x112` | DeleteCharacterOk   | 856     | `int` (0x0C, não lido), `SELCHAR` (0x10)                                   | ✅ exe 7.57 |
| `0x114` | CharacterLoginOk    | 1712    | `short X` (0x0C), `short Y` (0x0E), `STRUCT_MOB` (0x10), `ushort IdDoJogador` (0x412), `ushort Clima` (0x414), `byte SkillBar2[16]` (0x416), `int Hold` (0x428) | ✅ exe 7.57 |
| `0xFDE` | SecondPasswordOk    | 32      | segunda senha aceita                                                       | ✅ |
| `0xFDF` | SecondPasswordFail  | 16      | segunda senha errada: o client volta a bloquear e mostra o erro dele       | ✅ |
| `0x119` | CharacterLoginFail  | 12      | o client mostra a mensagem dele                                            | ✅ |
| `0x11C` | AccountLoginFail    | 12      | tela de login: mostra o erro e destrava (sem ele fica em "conectando"; `0x11D` igual) | ✅ |
| `0x116` | CharacterLogoutOk   | 12      | volta para a seleção de personagens (resposta ao `0x215`)                  | ✅ |
| `0x36A` | Motion              | 20      | `short Movimento`, `short Parâmetro`: 14 comemorar, 15 lamentar; parâmetro 3 = efeito de LEVEL UP (0x4D3E70); movimento 100 solta um efeito | ✅ layout |
| `0x3B4` | SelCharEffect       | 16      | `int Slot`: na seleção, efeito em cima do personagem do slot (usado no Arch novo) | ✅ layout |
| `0x11A` | CreateCharacterFail | 12      | o client mostra a mensagem dele                                            | ✅ |
| `0x11B` | DeleteCharacterFail | 12      | o client mostra a mensagem dele                                            | ✅ |

> Atenção: no 7.57 o login aceito é o `0x10A` (no 7.54 era o `0x10E`, que o 7.57 não trata mais).

> Tamanhos: o WYD.exe tem uma tabela com o tamanho esperado de cada pacote que recebe. Se não bater,
> ele só grava "Type check missing" no log dele e segue; mesmo assim o servidor usa os tamanhos exatos.

### `0x364` CreateMob (232 bytes)

| Offset | Campo | Offset | Campo |
|-------:|-------|-------:|-------|
| `0x0C` | `short X, Y` | `0x42` | `short Affect[32]` |
| `0x10` | `ushort Id` | `0x82` | `ushort Guild` |
| `0x12` | `char Name[12]` | `0x84` | `SCORE Score` |
| `0x1E` | `byte ChaosPoint, CurrentKill` | `0xB8` | `ushort CreateType` |
| `0x20` | `ushort TotalKill` | `0xBA` | `byte Refino[16]` |
| `0x22` | `ushort Equip[16]` | `0xCA` | `char Tab[26]` |

### Structs (7.57)

- **`STRUCT_ITEM`** (8 bytes): `short Index`, depois `{byte Effect, byte Value}` × 3.
- **`STRUCT_SCORE`** (48 bytes): `int Level, Ac, Damage`, `byte Merchant, AttackRun, Direction, ChaosRate`,
  `int MaxHp, MaxMp, Hp, Mp`, `short Str, Int, Dex, Con`, `short Special[4]`. O client limita vida e mana máximas em
  100000 (0x53CF0A). O dano de cada golpe no ataque é um int.
- **`STRUCT_SELCHAR`** (840 bytes, os 4 slots da conta):

  | Offset  | Campo                  |
  |--------:|------------------------|
  | `0x000` | `short SPX[4]`         |
  | `0x008` | `short SPY[4]`         |
  | `0x010` | `char Name[4][16]`     |
  | `0x050` | `SCORE Score[4]`       |
  | `0x110` | `ITEM Equip[4][16]`    |
  | `0x310` | `short Guild[4]`       |
  | `0x318` | `int Coin[4]`          |
  | `0x328` | `long Exp[4]`          |

  `Equip[0]` é o "rosto" do personagem (1 = TK, 11 = FM, 21 = BM, 31 = HT).
- **`STRUCT_MOB`** (1024 bytes, a ficha completa do personagem):

  | Offset  | Campo | Offset  | Campo |
  |--------:|-------|--------:|-------|
  | `0x000` | `char Name[16]` | `0x08C` | `ITEM Equip[16]` |
  | `0x010` | `byte Clan` | `0x10C` | `ITEM Carry[64]` |
  | `0x011` | `byte Merchant` | `0x30C` | `uint LearnedSkill[2]` |
  | `0x012` | `ushort Guild` | `0x314` | `short ScoreBonus, SpecialBonus, SkillBonus` |
  | `0x014` | `byte Class` | `0x31A` | `byte Critical, SaveMana` |
  | `0x016` | `ushort Quest` | `0x31C` | `byte SkillBar[4]` (`0xFF` = vazio) |
  | `0x018` | `int Coin` | `0x320` | `byte GuildLevel, Magic, RegenHP, RegenMP` |
  | `0x020` | `long Exp` | `0x324` | `byte Resist[4]` |
  | `0x028` | `short SPX, SPY` | `0x328` | 216 bytes novos (o servidor manda zerados) |
  | `0x02C` | `SCORE BaseScore` | | |
  | `0x05C` | `SCORE CurrentScore` | | |

## Como validar um layout com o client real

1. Ligue `"LogPackets": true` e conecte com o client.
2. Para pacotes **recebidos**: compare o hex do log com a tabela acima (offsets contados a partir do
   início do pacote, cabeçalho incluso).
3. Para pacotes **enviados**: se o client travar, fechar ou mostrar dados embaralhados, o tamanho ou a
   ordem dos campos está diferente. Os layouts ficam todos em
   [`Packets.cs`](../src/WYD.Server/Protocol/Packets.cs), e o `PacketWriter` se recusa a montar um
   pacote cujo tamanho não bata, então os erros aparecem logo.
4. Quando confirmar um pacote, troque ⚠️ por ✅ aqui.

## Segunda senha

O client guarda o estado da senha numérica numa variável (0 = bloqueado, painel aberto). Ao enviar a
senha ele **já desbloqueia por conta própria**; é a resposta `0xFDF` que faz voltar a bloquear. Por isso
o servidor também recusa criar, apagar e entrar no mundo até a senha ser conferida.

- Conta sem segunda senha: a primeira senha digitada passa a ser a da conta.
- Trocar: o client confere a atual (`Change = 0`), pede a nova duas vezes e manda com `Change = 1`.
- 5 erros seguidos desconectam.
- Esqueceu? `WYD.Server reset-second-password <conta>` apaga a senha; a próxima digitada vira a nova.

## Mundo

Cada jogador vê os outros num quadrado de 16 células em volta (o client desenha 33x33). Entrar no
campo de visão manda o `0x364` para os dois lados; sair manda o `0x165`. O destino de cada movimento
vira a posição do jogador (destinos a mais de 40 células são ignorados), e a posição é salva no banco
ao sair do jogo e ao desligar o servidor.

## Monstros

Os monstros e NPCs usam ids a partir de 1000 (os jogadores ficam abaixo). Eles aparecem com o mesmo
`0x364` dos jogadores, com o modelo no `Equip[0]`, e andam com o `0x366` gerado pelo servidor. A rota é
uma sequência de dígitos no estilo do teclado numérico, um por célula: `8` = +Y, `2` = −Y, `6` = +X,
`4` = −X, `9` = +X+Y, `7` = −X+Y, `3` = +X−Y, `1` = −X−Y (até 23 passos).

## Itens

Efeitos do ItemList.bin usados pelo servidor: `4` = cura HP, `5` = cura MP, `38` = item usável
(`1` = poção, `185` = barra de gold: 4010 vale 100 milhões e 4011 vale 1 bilhão), `61` = quantidade
de itens empilhados. Equipar confere a máscara `Pos` (bit *n* = slot *n* do Equip) e o nível mínimo.
Comprar cobra o preço do ItemList.bin; vender devolve 1/4 dele. O gold máximo é 2 bilhões.

## Experiência, nível e evolução

O `WYD.exe` 7.54 tem duas tabelas seguidas em `0x5CAF9C`:

- Mortal/Arch: 402 números de 32 bits sem sinal (500, 1124, 1826, 2610 … 4.290.000.000). `T[n]` é a
  experiência acumulada que o nível `n` precisa para virar `n + 1` (o nível 0 aparece como 1 na tela).
- Celestial/Sub Celestial: 201 números começando em 0 (`C[n]` = experiência no começo do nível `n`).

A experiência é um número sem sinal no client (até 4,29 bilhões). O comando `extract-exptable` copia as duas
tabelas para `data/`; sem elas ninguém sobe de nível.

A evolução fica no rosto (`Equip[0]`), no segundo valor de efeito: o client (função em `0x50437E`) trata 0
como Mortal, outro valor como evoluído, e usa a tabela de Celestial se a capa (`Equip[15]`) for uma das
capas de Celestial (3197, 3198, 3199, 573, 1767, 1770). O servidor usa 0 Mortal, 1 Arch, 2 Celestial e
3 Sub Celestial. Nível máximo: 400, 400, 200 e 200; no máximo, falar com o rei passa para a próxima com nível 1 e
experiência zerada, mantendo atributos e pontos.

Pontos (`0x277`): atributo gasta `ScoreBonus` (CON dá +2 de vida máxima, INT +2 de mana máxima), especial
gasta `SpecialBonus` (até 255). O servidor responde com `0x336` e `0x337`.

Ao matar um monstro o jogador recebe a `Exp` e o `Coin` da ficha do monstro e, a cada nível: 5 pontos de
atributo, 1 especial, 1 de skill e vida/mana máximas a mais (TK +5/+1, FM +2/+4, BM +3/+3, HT +4/+2), com
vida e mana cheias. O servidor manda `0x337` (exp, gold e pontos) e, se subiu, `0x336` e `0x181`.

## Monstros revidando, morte e renascer

Um monstro que leva dano passa a perseguir quem bateu: anda até ficar a 2 células, revida 0,3 s depois do
primeiro golpe e bate a cada 1,2 s (encurtado pela velocidade de ataque do NPC, 4 bits de cima do AttackRun: 15 = 0,48 s) com
o `0x39D` (ele como atacante, skill -1, movimento 4, 5 ou 6 em 0x1C, o jogador como alvo com o dano; o movimento vai
direto para a animação do atacante, 0x485A10, e 0xFF deixa o monstro batendo parado). Quem apanhou recebe também o `0x181` com
a vida nova. Com a vida em zero, o servidor manda `0x338` com o jogador como morto: o client mostra a morte e,
quando o jogador clica para renascer (ou 3 minutos depois), manda o `0x289`. O servidor enche vida e mana e
leva o personagem para `StartX`/`StartY` com um `0x366` de teleporte.

Os vivos recuperam 5% da vida e da mana máximas a cada 6 segundos (`0x181`).

## Reinos e reis

O reino é a capa (`Equip[15]`), pela mesma tabela do client (função `0x564783`):

| Reino | Número | Capas | Rei |
|-------|--------|-------|-----|
| Azul (Hekalotia) | 7 | 543, 545, 1766, 1767, 1768, 3191, 3194, 3197 | Rei Harabard (1755, 1573), oráculo azul no castelo |
| Vermelho (Akeronia) | 8 | 544, 546, 1769, 1770, 1771, 3192, 3195, 3198 | Rei Glantuar (1757, 1880), oráculo vermelho no castelo |
| Neutro (Adventurer) | 6 | 548, 549, 573, 3193, 3196, 3199 | qualquer um |

Os reis têm `Merchant` 15 e o clã do reino (7 ou 8). Clicar neles faz o client mandar o `0x28B`
(20 bytes: `int IdDoNpc`, `int Confirma`).

Os dados originais dos reis derrubam o client 7.54 (testado: tirando o rei do mapa com `/gm npc remover` o crash
some; trocar só o visual ou só o tipo não resolveu). Por isso o `0x364` dos reis vai com o visual e a ficha do
Guarda_Real do castelo (rosto 60 ou 305, armadura 165-168 ou 170-173, capa 734 ou 735), mantendo o nome e o
`Merchant` do rei. O NPCGener traz cada rei duas vezes no mesmo lugar; NPC parado repetido nasce uma vez só.

Os Oracles (`BlueOracle` em 1760,1582 e `RedOracle` em 1760,1890) fazem a Eternal Stone (1742) com Unicorn's
Spirit (1740), Phoenix's Spirit (1741) e 10 Sapphires (697). Com o tipo original (`Merchant & 0xF` = 14) o client
(0x45D9DD) só abre a pergunta se tiver 20 safiras, os dois Spirits em slots vizinhos e nível 300, e então manda o
`0x28B` com Confirma 1; o servidor manda o Oracle como tipo 13, que sempre abre a pergunta (`0x28B` com Confirma 0),
e confere a receita ele mesmo. O `BlueOracle` vem vazio em alguns pacotes de dados e nasce como cópia do `RedOracle`.

## Trocar personagem e criação do Arch

`0x215` (só o cabeçalho) é o "trocar personagem" do client; o servidor tira o personagem do mundo e responde com
`0x116` (só o cabeçalho, id do jogador no cabeçalho). O client (0x4805C3) volta para a seleção usando a lista que
recebeu no login (atualiza só a ficha e o equipamento do personagem que estava em jogo). Na seleção, um `0x110`
com a lista completa redesenha os quatro personagens (0x4A6CD2): é assim que o Arch novo aparece.

Para falar com o rei, o client de um Mortal exige a Eternal Stone (1742) no slot 10 e um Sefirot (1760-1763) no
slot 11 (0x45D86B); sem isso o clique não manda nada. Arch: rostos 6-9 (Transknight), 16-19 (Foema), 26-29 (Beastmaster), 36-39 (Hunter); o servidor usa
`10 × corpo + 6 + classe das skills`. O nome do personagem deixou de ser único no banco: dentro da mesma conta o
Arch repete o nome do Mortal.

## Refinação (Oriharukon e Laktorerium)

Arrastar o pó no item manda o `0x373` (como as gemas). Chance do client (0x558A00): tabela[material][grau − 1]
[refino] + bônus[refino] × falhas, com o grau no efeito 87 do item (1 a 6) e as tabelas em 0x5C80EC
(Oriharukon), 0x5C820C (Laktorerium) e 0x5C832C (bônus: 5, 5, 5, 4, 4, 3, 3, 2, 1). Abaixo do +10 o efeito 43
guarda o refino nas unidades e as falhas nas dezenas (o client só lê falhas até o valor 210). O Oriharukon tem 0%
do +6 em diante e o Laktorerium do +9. Montarias (2330-2389), joias (3200-3299) e cavalos com prazo não refinam.

| Grau | Oriharukon +0→+6 | Laktorerium +0→+9 |
|------|------------------|-------------------|
| 1 | 100 100 100 100 100 100 | 100 100 100 100 100 100 90 59 41 |
| 2 | 100 100 100 100 100 74 | 100 100 100 100 100 100 80 51 36 |
| 3 | 100 100 100 100 100 68 | 100 100 100 100 100 100 70 47 30 |
| 4 | 100 100 100 90 80 53 | 100 100 100 100 100 80 40 22 10 |
| 5 | 100 100 100 85 70 40 | 100 100 100 100 100 56 35 18 5 |
| 6 | 100 100 100 85 70 40 | 100 100 100 100 100 56 25 18 5 |

As linhas da tabela têm 12 colunas (o índice é o refino % 12). No Laktorerium a coluna 9 (+9→+10) é 0 e a 10
(+10→+11) é 3 em todos os graus. Do +10 em diante não há falhas guardadas (o efeito 43 é 230 + 4 × (refino − 10) +
gema), então a chance fica nos 3% e a falha deixa o item igual. O Laktorerium 100 (4141, mesmo uso 5 do pó comum)
passa o +10 direto para +11.

## Efeitos (buffs)

Os ícones do canto com o tempo vêm do `0x3B9`. O efeito visual (como a aura do Trovão) é uma flag que o client
liga ao receber o `0x336`/`0x364` com o efeito (0x52D286; tipo 22 → flag 0x752, que desenha raios a cada 0,5 s).
Os golpes da aura do Trovão vão como `0x36C` da Thunderbolt (33). Ao sair, os efeitos são gravados com o tempo que
falta (coluna `affects`) e voltam no próximo login com o relógio parado no meio-tempo.

O client guarda até 16 efeitos por personagem, cada um num `short`: tipo no byte de cima e o tempo que falta no
de baixo, em unidades de 8 segundos (0x5359B6 compara e reinicia o relógio do ícone). Vão no `0x336` (0x2A, os
ícones do próprio jogador) e no `0x364` (0x42). O tipo é o AffectType da SkillData.bin (campo 9; o TickType, campo
7, nas auras sem AffectType), com valor no campo 10 e tempo no 11.

## Velocidade e montarias

O client calcula a velocidade (função em `0x55A392`): `CurrentScore.AttackRun & 0xF` + soma do efeito 29 dos
itens (botas); se a montaria do slot 14 tiver vida, vale a velocidade dela; no máximo 6. O servidor manda a
`CurrentScore` com essa velocidade no `0x114`, no `0x364` e no `0x336` (depois de trocar equipamento).

Montaria (2360 a 2389): vida nos bytes 2-3 do item (short), nível no byte 4. A tabela em `0x5CA4C0` tem
6 inteiros por montaria (dano, magia, dois bônus, velocidade, ?): velocidade 4 para Porco, Javali e Urso, 5 para
Lobo, Filhote de Dragão e Smilodon, 6 do Cavalo em diante. Os cavalos com prazo (3980 a 3998) correm a 6.
Sem vida a montaria não dá bônus.

O `/kingdom` leva para os pontos `KingdomBlue`, `KingdomRed` e `KingdomNeutral` do `appsettings.json`, ou para os
que o GM marcou no jogo com `/gm reino` (gravados em `data/kingdoms.txt`).

## Efeitos dos itens e refinação

Como o client calcula (funções em `0x556F5B` e `0x558FF0`):

- Um efeito de um item = soma do efeito no ItemList + nos 3 efeitos do item (bytes com sinal).
- Refinação (efeito 43, `0x55845F`): de 0 a 229 vale o último dígito (+0 a +9); de 230 em diante,
  230 = +10, 234 = +11, 238 = +12, 242 = +13, 246 = +14, 250 = +15. Itens 1 a 40 não refinam; com +9 ou mais
  e `Pos & 0xF00`, conta um a mais.
- Até +10 o valor é multiplicado por (refinação + 10) / 10; de +11 a +15 por 2,2 / 2,5 / 2,8 / 3,2 / 3,7.
  Não multiplicam: 1, 17, 18, 21-25, 27, 33, 38, 47, 48, 78, 84, 86, 87, 88, 112, 113. Velocidade (29) fica em
  no máximo 2 (+1 com +9); regeneração (47, 48) é multiplicada pela refinação.
- Duas armas (slots 6 e 7): a mais forte conta inteira e a outra 30% (50% se forem do mesmo tipo).

O servidor soma ao base: dano (2), defesa (3), HP (4), MP (5), FOR/INT/DES/CON (7-10), especiais (11-14 e 74).

## Teleportes das cidades

O client sabe pelo mapa dele (atributo da célula, flag `0x10`) que o jogador está num círculo de teleporte,
pede a confirmação e manda o `0x290`. O destino e o preço não estão no client: o servidor procura o círculo
mais perto (até 4 células) em `data/teleports.txt` ("x y destinoX destinoY preço nome"), cobra e teleporta.
O GM monta a lista no jogo com `/gm tp add`.

O client também tem as áreas das cidades em `0x5C7E58` (Armia 2052-2171 × 2052-2163, Azran 2432-2675 ×
1536-1767, Erion 2448-2476 × 1966-2024 e mais duas) e manda o `0x291` ao mudar de área.

## Gemas e itens ancient

Os itens `(Ancient)` vêm em grupos de 4 cópias seguidas no ItemList, iguais a não ser pelo `Grade` (5 a 8).
O `0x373` traz o item usado em `0x0C`/`0x10` e o item de destino em `0x14`/`0x18` (tipo e slot), além de
`short X, Y` em `0x1C`. Arrastar uma gema (uso 180 a 183 = Grade + 175) num ancient troca o item pela cópia do
grupo. As pedras 2441 a 2444 (uso 9) são material de composição, não gemas.

| Gema | Grade | Efeito (com o item +10 ou mais equipado) |
|------|-------|-------------------------------------------|
| Diamond_Gem (3386) | 5 | +10% de experiência |
| Emerald_Gem (3387) | 6 | absorção: 10% a menos de dano recebido |
| Coral_Gem (3388) | 7 | perfuração: ignora 10% da defesa do alvo |
| Granet_Gem (3389) | 8 | +10% de drop (quando os drops existirem) |

## Mestres de skill

Mestres de skill têm `Merchant` 19 no arquivo do NPC e 3 em `Score.Merchant`: o client pede a lista deles com o
`0x27B` (como numa loja) e o servidor responde com o `0x17C` dos itens de skill que eles carregam, com o tipo
3 em `0x0C` (1 abre a loja; 3 abre o menu de skills, com as abas montadas pela classe do primeiro item). Ao escolher
uma skill e confirmar, o client manda o `0x277` com tipo 2, `short Qual` = item da skill (5000 + n) e o id do NPC.

A skill n é o item 5000 + n: TK 0-23 (Cap.Cavaleiros), FM 24-47 (Foema_Ancian), BM 48-71 (Mestre_Archi), HT 72-95
(ForeLearner). A aprendida vira o bit `n % 24` do `LearnedSkill` (`0x337`). O ItemList dá a classe (efeito 18),
o nível mínimo e, nos 4 campos de requisito (FOR/INT/DES/CON nos outros itens), os Especiais 1 a 4 mínimos: a 1ª
árvore pede Especial 2, a 2ª Especial 3 e a 3ª Especial 4 (ex.: Immunity do HT pede nível 60 e 90 no Especial 2); a `SkillData.bin` (registros de 96 bytes em XOR 0x5A, 104 skills + checksum)
dá o custo em pontos de skill no primeiro int de cada registro.

## Atributos na ficha atual

Como o client calcula (função em `0x55A392`, a mesma conta que o servidor faz em `Character.CurrentScore`):

- Dano = dano base + itens (efeito 2) + FOR/3 + DES/8 + Especial 1 + nível (o dobro do nível quando
  `rosto % 10 > 5`).
- Defesa = defesa base + efeitos 3 e 53.
- Vida máxima = (base + efeito 4 + CON × 2) × (100 + efeito 45) / 100; mana = (base + efeito 5 + INT × 2) ×
  (100 + efeito 46) / 100 (os efeitos 45 e 46 são %; o "× 2" de CON/INT é deste servidor).
- Velocidade de ataque: DES/5 + efeito 26 (o client calcula sozinho).
- Regeneração: o servidor soma os efeitos 47 e 48 (anéis) aos 5% de cada 6 segundos.
- Magia (60 + 68), crítico (42), economia de mana (44) e resistências (49-54) ficam guardados para as skills.

## Usar skills

O client manda o ataque (`0x39D`/`0x39E`/`0x36C`) com o número da skill em `0x18` (-1 = ataque normal). O servidor
confere se a skill é da classe e foi aprendida, gasta a mana (`ManaSpent` da SkillData, menos a economia de mana
dos itens) e devolve a mana atual em `0x1A` do próprio ataque e no `0x181`.

Efeito pela SkillData (`InstanceType`, `InstanceValue`):

| Tipo | Efeito | Conta (deste servidor) |
|------|--------|-------------------------|
| 1 | dano físico | dano normal + força da skill + especial da árvore |
| 2-5 | dano mágico de elemento | (força + especial da árvore + INT/4) × (100 + magia dos itens)% ± 10%, menos defesa/8 |
| 6 | cura (em si ou em quem você vê) | força + especial da árvore + INT/4, com a magia dos itens; aparece como dano negativo |

Buffs (`AffectType`, `AffectValue`, `AffectTime`) ainda não fazem efeito.

## Sefirot (Eternal Stone no mestre de skill)

Com a Eternal Stone (1742) no slot 10, clicar num mestre de skill não pede o menu: o client abre a pergunta
"deseja continuar?" e manda o `0x28B` com `Confirma` = 0; ao aceitar, manda de novo com 1. Com as 8 pedras
(Kocuna, Veean, Keced, Gebra, Tipalet, Nechark, Yesord, Marcute: 1744 a 1751) no inventário e 30 milhões de gold,
o servidor troca tudo pelo Sefirot da classe do mestre: 1760 TK, 1761 FM, 1762 BM, 1763 HT.

## NPCs com itens que o client não tem

O client 7.54 fecha (uns segundos depois) ao desenhar um NPC com um item que não existe no ItemList dele. Os
arquivos de NPC do servidor 7.56 têm alguns: o Black Oracle e o Red Oracle dos castelos (item 13 no elmo) e o
Argus de Noatun (rosto 348). Por isso o servidor troca por 0, no `0x364`, todo item de NPC vazio no ItemList.

## Efeito da arma acima de +12

O client escolhe o efeito da arma como `Grade (offset 138 da ItemList) * 13 + refinação` (0x52482A). Acima de +12 a
conta passa para o efeito do Grade seguinte, por isso armas +13 a +15 (ancient ou não) soltam o efeito errado. O
comando `patch-client` troca as quatro somas (`add al, [edx+0x18C|0x18D]` em 0x524865, 0x524904, 0x52498C e
0x524A0F) por uma chamada a um trecho novo no fim da `.text` (0x5AC840 e 0x5AC860) que soma no máximo 12. O nome e
a refinação do item não mudam.

## WYDLauncher

O launcher lê o endereço de patch na primeira linha do `Settings.txt` (o resto do arquivo são os textos da janela) e
baixa `endereço + update1.htm` com `InternetOpenUrlA` (até 8 KB; o launcher grava a cópia em `update.dat`). A
resposta precisa começar com `[WYD]`; cada linha seguinte é `versão arquivo [arquivo...]` (versão abaixo de 1000 é
multiplicada por 10). A primeira linha é a versão mínima: se o `[VERSION]` do `config.txt` for menor, o launcher
manda baixar o jogo do site. Os arquivos das linhas com versão maior que a do `config.txt` são baixados de
`endereço + arquivo` para a pasta do jogo (um `.zip` é extraído com os caminhos de dentro e apagado), e a versão
salva no `config.txt` sobe junto. Uma linha `NOTICE largura altura url` troca a página mostrada na janela.

Os arquivos são baixados com `InternetConnectA` na porta 80 fixa (o endereço é quebrado nas três primeiras `/`, sem
ler porta) e `HttpOpenRequestA` com o caminho sem a `/` inicial. A resposta precisa ter status 200 e
`Content-Length` maior que zero.

## Drop

O drop de cada monstro está nos 64 slots do `Carry` do arquivo de NPC (0x10C). Efeitos de 100 em diante nesses
itens (101 a 104) marcam adicionais sorteados e saem do item. No 7.54 não existe item no chão: o que cai vai direto
para o inventário de quem matou (0x182, inventário). O client ainda manda o `0x272` (largar, 32 bytes: int tipo,
int slot, int rotação, short X, short Y) ao arrastar um item para fora; o servidor ignora e o item fica no slot.

## Tamanho dos itens no inventário

O efeito 33 do item (0 a 7) escolhe um par largura × altura na tabela `{int, int}[8]` em 0x5C88F4 (na `.data`):
1×1, 1×2, 1×3, 1×4, 2×1, 2×2, 2×3, 2×4. O client usa a mesma tabela no ícone (0x40C916), na busca de slot livre
ao pegar item (0x52318D) e no encaixe da grade (0x4230F6, 0x423605). Os slots ocupados vêm de outra tabela, de
máscaras `byte[8]` (2 colunas × 4 linhas) em 0x5C8934, uma por tamanho, que o client percorre para montar o mapa do
inventário (64 slots) e do baú (128) em 0x55D797/0x55D9B4. O `patch-client` põe 1×1 nos 8 pares e só o primeiro
quadrado nas 8 máscaras: todo item ocupa 1 slot (o slot de um item é sempre linha × 9 + coluna; o inventário tem
63, 9 × 7).

## Velocidade de ataque e crítico

A velocidade de ataque vai nos 4 bits de cima de `AttackRun` da ficha atual. O client calcula (0x55A70A a 0x55CA3B)
10 × os 4 bits de cima da base + efeito 26 dos itens + transformação + DES / 5, menos 30 com lentidão (efeito 1) e
valor / 10 + 10 com congelar (efeito 7); limita entre 0 e 150 e divide por 10. A animação usa o valor da ficha que
o servidor manda no `0x336` (0x55FF5B: (velocidade − 5) × 100 ms), então o servidor faz a mesma conta.

O crítico (efeito 42) e a economia de mana (efeito 44) vão nos bytes 0x28 e 0x29 do `0x336` (0x2E6 e 0x2E7 do
STRUCT_MOB). O client mostra crítico × 0,4 % (0x445CDB) e não calcula o crítico fora das transformações; o servidor
manda a soma dos itens e dá o dobro do dano com chance de crítico × 4 em 1000.

### Troca de Espírito e Controle de Mana

O client calcula a ficha com os efeitos (0x55A392). A Troca de Espírito (efeito 38, skill 87) passa para a vida
máxima parte da mana máxima: mana / 4 se o rosto é evoluído (rosto % 10 > 5, 0x56475A) ou / 8 no Mortal, mais
(Especial 3 + nível) / 4, com 400 a mais no nível se o bit 30 das skills aprendidas estiver ligado; a vida máxima
ganha ainda 750. Isso entra antes dos efeitos 45/46 (% de vida e mana). O Controle de Mana (efeito 18), no client,
só liga uma flag (bit 0 em 0x55BC18).

## Skills de Sephira e Soul

As skills 96 a 103 da SkillData são as de Sephira, iguais para todas as classes, nos bits 24 a 31 do `LearnedSkill`
(97 Conjure Cannon, 98 Thorn Wall, 99 Resurrect, 100 Concentration, 101 Spectral Force, 102 Soul of Limits). Os
livros 667 a 671 têm uso (efeito 38) 32 a 36 e ensinam as skills 97 a 101. O client trata rosto evoluído + bit 30
(Soul) como Celestial só em código oficial que o client nunca chama (0x555776, 0x555987, 0x555F7C); os testes do
bit nos lugares chamados são constantes. O Arch pode ter a Soul sem virar Celestial; o Celestial já nasce com ela.

Efeitos das outras (todos no servidor, menos o alcance, que o client também soma):

- 97 Canhão Guardião: usa o canhão do castelo de Noatun, só na guerra de castelos. Sem a guerra, o servidor recusa.
- 98 Muro de Espinhos (skill ofensiva: o `0x39D` com a skill 98 e o lugar em 0x14/0x16): 3 plantas (modelo Vine,
  358) em fila na perpendicular a quem lança, por 30 s, com 300 + 20 × nível de vida. Ocupam a célula, podem ser
  mortas, não dão exp nem drop e não renascem; um muro novo tira o anterior.
- 99 Ressurreição: morto, quem recusa voltar para a cidade (o `0x289` continua levando para a cidade) clica com o
  botão direito e confirma o diálogo 99; 5 s depois o client faz a animação e manda o `0x39D` com a skill 99 nele
  mesmo (0x472329). O servidor o traz de volta ali com 30% da vida.
- 100 Concentração (passiva): o ataque não erra pela esquiva do alvo. Hoje os ataques do jogador não erram.
- 101 Força Espectral (passiva): +1 de alcance em armas e skills. O client soma o bit 29 ao alcance da arma
  (0x490525); o servidor aceita uma célula a mais.

A Soul (efeito 29, o mesmo da Maestria em Armas) aumenta os atributos da combinação escolhida em % deles mesmos
(código oficial em 0x55BEE6, tabelas de salto 0x55CD36 e 0x55CD5E): combinações 1 FOR/CON, 2 INT/CON, 3 DES/CON,
4 FOR/DES, 5 INT/DES, 6 CON/DES, 7 FOR, 8 INT, 9 CON, 10 DES; Celestial 80% + 60% (simples 120%), Mortal 40% + 30%
(simples 60%). O servidor dá 400 s de buff e 15 minutos de espera.

### Kibita, Odin e Ehre

O tipo de NPC que o client usa é `Score.Merchant & 0xF`. A Kibita tem 10 (0x4A): o client pergunta e manda o
`0x28B`, como no rei (0x45DB35). O Odin tem 7 (0x17): nos tipos 6 a 8 o client manda o `0x28E` (16 bytes, int id do
NPC em 0x0C; 0x45E0A4). A Ehre tem 0 no arquivo; o servidor a manda com o tipo 13 e ela não pode ser atacada. O client abre a janela de
combinação dela e, no OK, manda o `0x2D3` (84 bytes: `ITEM Item[8]` em 0x0C e `sbyte Slot[8]` em 0x4C, o slot do
inventário de cada item, -1 vazio; 0x46290B). O `0x3A7` (só o cabeçalho) fecha as janelas de combinação (0x484B87).

- Odin: as 7 runas do elemento da classe (Vento HT, Água TK, Terra BM, Sol FM; itens 5110 a 5133) e 2.000.000 de
  gold viram a Pedra Secreta (5334 Água, 5335 Terra, 5336 Sol, 5337 Vento).
- Kibita: Mortal nível 371+ ou Arch de qualquer nível com a Pedra Secreta da classe ganha a Soul (bit 30) e o Manto do Herói do reino (3194
  azul, 3195 vermelho, 3196 neutro; a capa antiga vai para o inventário) e volta para a seleção de personagens.
- Ehre: as 3 joias da janela (Diamante 2441/3386, Esmeralda 2442/3387, Coral 2443/3388, Garnet
  2444/3389), na ordem da janela, escolhem a combinação: DDD 1, EEE 2, CCC 3, GGG 4, DEC 5, DCG 6, ECG 7, EDC 8,
  CDC 9, GDC 10.

### Capas por evolução

Mortal e Arch usam as capas comuns. O Celestial e o Sub Celestial usam as Master Of Hekalotia (3197, azul),
Akeronia (3198, vermelha) e Adventurer (3199, branca), com modelo próprio (2880). Ao virar Celestial o servidor
veste a Master do reino da capa atual e põe a capa antiga no inventário; só Celestial e Sub Celestial equipam as
Master.
