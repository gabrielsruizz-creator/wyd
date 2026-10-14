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
