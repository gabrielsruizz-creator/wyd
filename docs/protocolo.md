# Protocolo do client 7.54 / 7.56

Como o servidor conversa com o client. Tudo é little-endian, e as strings são `char[N]`
de tamanho fixo terminadas em zero.

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
| `0x20D` | AccountLogin       | 116     | `Account[16]`, `Password[12]`, `int Version`, `int DBNeedSave`, `int Adapter[4]`, `Zero[52]` | ✅ |
| `0x20F` | CreateCharacter    | 36      | `int Slot`, `Name[16]`, `int Class`                                      | ✅ |
| `0x211` | DeleteCharacter    | 44      | `int Slot`, `Name[16]`, `Password[12]`                                   | ✅ |
| `0x213` | CharacterLogin     | 36      | `int Slot`, …                                                            | ✅ |
| `0xFDE` | SecondPassword     | 32      | `Password[16]`, `int Change` (0 = conferir, 1 = gravar nova)             | ✅ |
| `0x366` / `0x367` | Movimento | 52      | `short X, Y`, `int Velocidade`, `int Tipo`, `short DestX, DestY` (0x18), `char Rota[24]` | ✅ |
| `0x369` | RequestMob         | 16      | `ushort Id` (0x0C): o client pede o 0x364 de um id que não conhece       | ✅ |
| `0x333` | Chat               | 108     | `char Texto[96]`                                                         | ✅ |
| `0x334` | Comando / sussurro | 128     | `char Cmd[16]` (0x0C) e o resto do texto (0x1C): "/item 905" chega como Cmd = "item", texto = "905" | ⚠️ layout lido no WYD.exe, falta ver no client |
| `0x39D` / `0x39E` / `0x36C` | Ataque | 48 / 52 / 96 | `ushort Atacante` (0x0C), posições, `short Skill` (0x18), alvos `{ushort Id, short Dano = -1}` a partir de 0x2C | ✅ layout |
| `0x277` | Distribuir ponto     | 20      | `short Tipo` (0 atributo, 1 especial, 2 skill no mestre), `short Qual` (0 FOR/1 INT/2 DES/3 CON ou especial 1-4), `short Extra` | ✅ |
| `0x28B` | Falar com NPC        | 20      | `int IdDoNpc`, `int Confirma` (reis, NPCs de quest) | ✅ layout |
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
| `0x101` | MessagePanel        | 108     | `char Text[96]`                                                            | ✅ |
| `0x10E` | AccountLoginOk      | 1824    | `SELCHAR` (0x00C), `ITEM Cargo[128]` (0x2F4), `int Coin` (0x6F4), `Account[16]` (0x6F8), `Keys[16]` (0x708) | ✅ |
| `0x364` | CreateMob           | 176     | põe um jogador na tela (layout abaixo)                                     | ✅ |
| `0x39D` / `0x39E` / `0x36C` | Ataque | 48 / 52 / 96 | o mesmo pacote com o dano de cada alvo; `int Hold` (0x20) e `int ExpDoAtacante` (0x24) atualizam a barra de experiência | ✅ layout |
| `0x338` | MobKill             | 24      | `int Hold`, `ushort Morto`, `ushort Matador`, `int ExpDoMatador`           | ✅ layout |
| `0x379` | BuyOk               | 24      | o pedido com o slot final e `int GoldTotal` (0x14), que o client copia para o gold | ✅ layout |
| `0x3AF` | UpdateCoin          | 16      | `int Gold`                                                                 | ✅ layout |
| `0x181` | UpdateHpMp          | 20      | `int Hp`, `int Mp`                                                         | ✅ layout |
| `0x36B` | UpdateEquip         | 60      | `ushort Equip[16]`, `byte Refino[16]` (para quem está vendo)               | ⚠️ |
| `0x337` | UpdateEtc           | 36      | `int Hold`, `int Exp`, `uint LearnedSkill`, `short ScoreBonus, SpecialBonus, SkillBonus`, 2 de folga, `int Gold` | ✅ layout |
| `0x336` | UpdateScore         | 92      | `SCORE` (28) @0x0C, Critical/SaveMana @0x28, `Affect[32]` @0x2A, Guild @0x4A, GuildLevel @0x4C | ⚠️ |
| `0x182` | UpdateItem          | 24      | `short Tipo` (0 equip, 1 inventário, 2 baú), `short Slot`, `ITEM`          | ✅ |
| `0x334` | Sussurro            | 128     | `char De[16]`, texto                                                       | ⚠️ |
| `0x17C` | ShopList            | 236     | `int Tipo` (1 = itens), `ITEM Lista[27]`, `int Imposto`; aba *n* vem do `Carry[n*27 .. n*27+8]` do NPC | ✅ |
| `0x165` | RemoveMob           | 16      | id no cabeçalho, `int Tipo` (0x0C)                                         | ✅ |
| `0x366` | Move                | 52      | o mesmo pacote do client, com o id de quem andou no cabeçalho; `int Tipo` (0x14) = 1 teleporta direto para (DestX, DestY) | ✅ |
| `0x333` | Chat                | 108     | o mesmo pacote do client, com o id de quem falou (não volta para ele)      | ✅ |
| `0x110` | CreateCharacterOk   | 756     | `SELCHAR`                                                                  | ✅ |
| `0x112` | DeleteCharacterOk   | 756     | `SELCHAR`                                                                  | ✅ |
| `0x114` | CharacterLoginOk    | 1244    | `short X` (0x0C), `short Y` (0x0E), `STRUCT_MOB` (0x10), `ushort IdDoJogador` (0x306), `ushort Clima` (0x308), `byte SkillBar2[16]` (0x30A), `int ?` (0x31C) | ✅ offsets; ⚠️ falta testar no client |
| `0xFDE` | SecondPasswordOk    | 32      | segunda senha aceita                                                       | ✅ |
| `0xFDF` | SecondPasswordFail  | 16      | segunda senha errada: o client volta a bloquear e mostra o erro dele       | ✅ |
| `0x119` | CharacterLoginFail  | 12      | o client mostra a mensagem dele                                            | ✅ |
| `0x11A` | CreateCharacterFail | 12      | o client mostra a mensagem dele                                            | ✅ |
| `0x11B` | DeleteCharacterFail | 12      | o client mostra a mensagem dele                                            | ✅ |

> Atenção: no 7.54 o login aceito é o `0x10E`. O `0x10A` que aparece em documentação de outras
> versões é ignorado por este client.

> Tamanhos: o WYD.exe tem uma tabela com o tamanho esperado de cada pacote que recebe. Se não bater,
> ele só grava "Type check missing" no log dele e segue; mesmo assim o servidor usa os tamanhos exatos.

### `0x364` CreateMob (176 bytes)

| Offset | Campo | Offset | Campo |
|-------:|-------|-------:|-------|
| `0x0C` | `short X, Y` | `0x42` | `byte Affect[32]` |
| `0x10` | `ushort Id` | `0x62` | `ushort Guild` |
| `0x12` | `char Name[12]` | `0x64` | `SCORE Score` |
| `0x1E` | `byte ChaosPoint, CurrentKill` | `0x80` | `ushort CreateType` |
| `0x20` | `ushort TotalKill` | `0x82` | `byte Refino[16]` |
| `0x22` | `ushort Equip[16]` | `0x92` | `char Tab[26]` |

### Structs (7.54)

- **`STRUCT_ITEM`** (8 bytes): `short Index`, depois `{byte Effect, byte Value}` × 3.
- **`STRUCT_SCORE`** (28 bytes): `short Level, Ac, Damage`, `byte Merchant, AttackRun`,
  `short MaxHp, MaxMp, Hp, Mp`, `short Str, Int, Dex, Con`, `byte Special[4]`.
- **`STRUCT_SELCHAR`** (744 bytes, os 4 slots da conta):

  | Offset  | Campo                  |
  |--------:|------------------------|
  | `0x000` | `short SPX[4]`         |
  | `0x008` | `short SPY[4]`         |
  | `0x010` | `char Name[4][16]`     |
  | `0x050` | `SCORE Score[4]`       |
  | `0x0C0` | `ITEM Equip[4][16]`    |
  | `0x2C0` | `short Guild[4]`       |
  | `0x2C8` | `int Coin[4]`          |
  | `0x2D8` | `int Exp[4]`           |

  `Equip[0]` é o "rosto" do personagem (1 = TK, 11 = FM, 21 = BM, 31 = HT).
- **`STRUCT_MOB`** (756 bytes, a ficha completa do personagem):

  | Offset  | Campo | Offset  | Campo |
  |--------:|-------|--------:|-------|
  | `0x000` | `char Name[16]` | `0x05C` | `ITEM Equip[16]` |
  | `0x010` | `byte Clan` | `0x0DC` | `ITEM Carry[64]` |
  | `0x011` | `byte Merchant` | `0x2DC` | `uint LearnedSkill` |
  | `0x012` | `ushort Guild` | `0x2E0` | `short ScoreBonus, SpecialBonus, SkillBonus` |
  | `0x014` | `byte Class` | `0x2E6` | `byte Critical, SaveMana` |
  | `0x016` | `ushort Quest` | `0x2E8` | `byte SkillBar[4]` (`0xFF` = vazio) |
  | `0x018` | `int Coin` | `0x2EC` | `byte GuildLevel, Magic, RegenHP, RegenMP` |
  | `0x01C` | `int Exp` | `0x2F0` | `byte Resist[4]` |
  | `0x020` | `short SPX, SPY` | | |
  | `0x024` | `SCORE BaseScore` | | |
  | `0x040` | `SCORE CurrentScore` | | |

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

Um monstro que leva dano passa a perseguir quem bateu: anda até ficar a 2 células e bate a cada 2 segundos com
o `0x39D` (ele como atacante, skill -1, o jogador como alvo com o dano). Quem apanhou recebe também o `0x181` com
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
