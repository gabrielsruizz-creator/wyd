# WYD.exe 7.57 (patch 7580, 2011-11-01) × 7.54 — o que muda no servidor

Comparação estática dos dois executáveis (o 7.57 montado com a base 7550 e os patches até o 7580). Endereços do 7.57
salvo indicação.

Confiança: **A** = lido no código com endereço; **M** = deduzido por alinhamento das duas versões / tamanho;
**B** = hipótese.

## 0. O que NÃO muda (A)
- Cifra: mesma rotina (7.54 0x4271fe / 7.57 0x4250fd), mesma tabela estática (7.54 0x5bbca4 = 7.57 0x5f6fe0,
  512 bytes idênticos), mesmo CheckSum (cifrado − claro, 0x4252f9). JProtectDll.dll igual (45056 bytes).
  → `data/KeyTable.bin` deve continuar valendo (a tabela real vem de fora do exe nas duas). Confirmar no 1º login.
- Código de abertura 0x1F11F311 (3 usos nas duas).
- Cabeçalho de 12 bytes; pacotes só-cabeçalho continuam 12.
- ITEM = 8 bytes (0x17C ShopList continua 236; 0x182 continua 24).
- Iguais no tamanho e no layout de envio do client: 0x20F (36), 0x211 (44), 0x213 (36) — construtores
  idênticos byte a byte; 0x215, 0x333 (108), 0x334 (128), 0x366 Move (52), 0x369, 0x36A (20), 0x36B (60),
  0x373 (36), 0x376 (20), 0x378 (32), 0x379 (24), 0x37A (20), 0x27B (16), 0x277 (20), 0x28B (20),
  0x28E (16), 0x290 (16), 0x289 (12), 0x272 (32), 0x165 (16), 0x185 (528), 0x116/0x119/0x11A/0x11B/0x11C (12).
- 0x3A7 (fecha combinação) e 0x2D3 continuam tratados (handler 0x3a7 → 0x489e63). Skills 97–101 (Sephira)
  idênticas no SkillData.
- Algoritmo do checksum de ItemList/SkillData igual (7.57 0x539cdb = 7.54 0x5599b2, só recebe o tamanho).

## 1. Login (A)
**0x20D (116 bytes) mudou de layout** — construtor 7.54 0x480834 / 7.57 0x48587e:

| campo | 7.54 | 7.57 |
|---|---|---|
| Password[12] (strcpy do global) | 0x1C | **0x0C** |
| Account[16] | 0x0C | **0x18** |
| bloco de 52 bytes (src+0x1C) | 0x40 | **0x28** |
| int Version | 0x28 = **754** (0x2F2) | **0x5C = 758** (0x2F6) |
| int DBNeedSave = 1 | 0x2C | 0x60 |
| int Adapter[4] | 0x30 | 0x64 |

O `[VERSION] 7571` do config.txt é só do launcher; o exe manda 758 fixo.
→ `AccountLoginRequest.Read` (Packets.cs) e `KeyTableFinder` precisam do layout novo; aceitar versão 758.

**Login aceito agora é 0x10A, 1928 bytes** (0x10E não existe mais no 7.57; handler 0x485b3f):
- 0x0C `byte Keys[16]` (copiado para o módulo de rede, como o 0x708 do 7.54)
- 0x1C 4 bytes (B: não lido)
- 0x20 `SELCHAR` (840 bytes)
- 0x368 `ITEM Cargo[128]`
- 0x768 `int CargoCoin`
- 0x76C..0x788: não lidos (B: Account[16] + 12)

**0x110 / 0x112 = 856 bytes**: `int ?` em 0x0C (B: não lido), `SELCHAR` em 0x10 (handler 0x4ac0ce copia 0x348 de +0x10).

**SELCHAR 7.57 = 840 (0x348) bytes** (A; offsets confirmados no handler e no zerar de slot 0x4ae1ba):
0x000 short SPX[4] · 0x008 short SPY[4] · 0x010 Name[4][16] · **0x050 SCORE[4] (48 cada)** ·
**0x110 ITEM Equip[4][16]** · **0x310 short Guild[4]** · **0x318 int Coin[4]** · **0x328 int64 Exp[4]**.

## 2. Pacotes servidor→client que mudaram (A, pela tabela de tamanhos 0x543xxx)

| op | 7.54 | 7.57 | notas |
|---|---|---|---|
| 0x10E→**0x10A** | 1824 | 1928 | acima |
| 0x110/0x112 | 756 | 856 | acima |
| 0x114 | 1244 | **1712** | MOB de 1024 |
| 0x181 | 20 | **28** | int Hp @0x0C, int Mp @0x10, +0x14/+0x18 (A: lidos; B: ReqHp/ReqMp), 4 livres |
| 0x336 | 92 | **144** | |
| 0x337 | 36 | **48** | |
| 0x338 | 24 | **32** | |
| 0x364 | 176 | **232** | |
| 0x367 | 52 | **168** | agora é o ataque em área |
| 0x36C | 96 | **52** | agora é a ação/parada |
| 0x18A | 16 | 20 | int @0x0C, word @0x10 (não usamos) |
| 0x1005/0x2006/0x3C0F/0x416–0x419 | | | cópias para outras cenas, mesmos números |

0x39D / 0x39E não estão na tabela (tamanho variável) — ver §3.

**0x114 CharacterLoginOk (1712)** — handler 0x485c1b: 0x0C short X, 0x0E short Y (→ MOB.SPX/SPY),
0x10 **STRUCT_MOB (0x400)**, 0x412 ushort id do jogador, 0x414 clima, 0x416 SkillBar2[16], 0x428 int (Hold,
mesmo destino do Hold do 0x337), resto não lido.

**STRUCT_MOB 7.57 = 1024 bytes** (A onde marcado):
0x000 Name[16] · 0x010 Clan · 0x011 Merchant · 0x012 ushort Guild · 0x014 Class (A) · 0x015 Rsv · 0x016 Quest ·
0x018 int Coin (A) · 0x01C int ? (B) · **0x020 int64 Exp** (A) · **0x028 short SPX, 0x02A SPY** (A) ·
**0x02C SCORE Base** (A) · **0x05C SCORE Current** (A) · **0x08C ITEM Equip[16]** (A) · **0x10C ITEM Carry[64]** (A) ·
**0x30C uint LearnedSkill[2]** (64 bits; A) · 0x314 ScoreBonus · 0x316 SpecialBonus · 0x318 SkillBonus (A) ·
0x31A Critical · 0x31B SaveMana (A) · **0x31C SkillBar[4]** (A) · 0x320 GuildLevel · 0x321 Magic · 0x322/0x323 Regen (M) ·
0x324 Resist[4] (A) · **0x328–0x3FF: 216 bytes novos, desconhecidos** (zerar).

**STRUCT_SCORE 7.57 = 48 bytes** (M/A): +0x00 int Level (lido como word), +0x04 int Ac, +0x08 int Damage (M),
+0x0C byte Merchant (A), +0x0D byte AttackRun (A), +0x0E/+0x0F bytes (B: Direction/ChaosRate),
**+0x10 int MaxHp, +0x14 int MaxMp, +0x18 int Hp, +0x1C int Mp** (A: 0x336, 0x53cf0a), +0x20 short Str, +0x22 Int,
+0x24 Dex, +0x26 Con (A), +0x28 Special[4] (B: provavelmente short[4], até 0x30).

**0x336 UpdateScore (144)** — 0x5113cc: SCORE(48) @0x0C · Critical @0x3C · SaveMana @0x3D ·
**Affect short[32] @0x3E** (laço de 0x20; no 7.54 short[16] @0x2A) · Guild @0x7E · GuildLevel @0x80 ·
word @0x86, word @0x88, byte @0x8A, byte @0x8E (= 0x52/0x54/0x56/0x5A do 7.54).

**0x337 UpdateEtc (48)** — 0x51469b: int Hold @0x0C · **int64 Exp @0x10** · **uint LearnedSkill[2] @0x18** ·
short ScoreBonus @0x20, SpecialBonus @0x22, SkillBonus @0x24 · int Gold @0x28 · 0x2C livre.

**0x338 MobKill (32)** — 0x488c39: int Hold @0x0C · ushort Morto @0x10 · ushort Matador @0x12 · **int64 Exp @0x18**.

**0x364 CreateMob (232)** (M, alinhamento 0x47e0bd×0x482e0e): 0x0C X, 0x0E Y, 0x10 Id, 0x12 Name[12],
0x1E ChaosPoint, 0x1F CurrentKill, 0x20 TotalKill, 0x22 ushort Equip[16], **0x42 Affect (64 bytes, short[32])**,
**0x82 Guild**, **0x84 SCORE(48)**, 0xB4 4 bytes (B), **0xB8 CreateType**, **0xBA Refino[16]**, **0xCA Tab[26]**, 0xE4 4 bytes.

## 3. Pacotes client→servidor que mudaram

**Opcodes trocados (A):** o que no 7.54 era `0x367` (ação/parada, 52 bytes) agora sai como **`0x36C` (52)**
(0x505b58: `jne → 0x366 senão 0x36C`); o ataque em área agora sai como **`0x367` (168 bytes)** (0x41dcfa, 0x4565da).
→ `Opcodes.MoveStop`/`Attack` invertem; o servidor também manda nos novos números.

**MSG_Attack 7.57** (construtor 0x41dcfa; leitura em 0x48a5dd alinhada com 0x485324):
- tamanhos: 1 alvo **0x48 (72)** = 0x39D; 2 alvos **0x50 (80)** = 0x39E; 13 alvos **0xA8 (168)** = 0x367 (A)
- 0x0C int Hold (A: 0x49110d) · 0x10 int ReqMp (M: subtraído da mana, 0x4936d5) · 0x14 int ? ·
  **0x18 int64 CurrentExp** (A: 0x491114/0x491118) · 0x20 ? ·
  **0x22 PosX, 0x24 PosY, 0x26 TargetX, 0x28 TargetY** (A) · **0x2A ushort AttackerID** (A) ·
  **0x2E byte Motion** (A, era 0x1C) · 0x2F/0x30/0x31 bytes (M: FlagLocal/DoubleCritical/SkillParm, eram 0x1E/0x1F/0x1D) ·
  **0x34 int CurrentMp** (M, era short 0x1A) · **0x38 short SkillIndex** (A, era 0x18) ·
  **0x3C STRUCT_DAM[13] de 8 bytes: int TargetID (lido como word) @+0, int Damage @+4** (A: `[x+i*8+0x3C]`, `[x+i*8+0x40]`) ·
  0xA4 int final (B).
- Dano agora é **int** (era short) → o limite de 32767 por golpe some.
→ `AttackLayout`, `AttackOneSize`, `MobAttack`, parse dos 0x39D/0x39E/0x367 precisam do layout novo.

## 4. Arquivos de dados (A)
- **ItemList.bin**: mesmo formato (6500 × 140 + 4, XOR 0x5A; 3209 itens, 1176 idênticos ao 7.54). Checksum esperado
  **0x381AF9B** (0x53a3cb e 0x53a4f7, antes e depois do ExtraItem). Loader 0x53a329; falha → `and` em 0x539a46 → o
  client não inicializa.
- **SkillData.bin**: 0x5D00 = 23808 + 4 → **248 registros de 96 bytes** (7.54: 104). Checksum esperado **0x184918**
  (0x53a2e3). Registros 0–103 iguais ao 7.54 exceto 16 (0,2,4,6,7,16–23,26,49,103: balanceamento); 200–247 novos (48 skills).
  Índice usado no ataque: `skill*0x60 + 0xEBA18C` (= campo +0x4C, nº de alvos).
- ⚠️ **Os arquivos do próprio patch 7580 NÃO batem com o exe 7580**: ItemList.bin dá **0x38139DD** (esperado 0x381AF9B) e
  SkillData.bin dá **0x183F90** (esperado 0x184918). Até o patch 7574 exe e listas batiam (0x382BC95). Ou seja, o client
  montado deve dar "Initialize Data Failed". Saídas: (a) acertar o checksum mexendo em bytes de itens/skills vazios
  ou (b) trocar as duas constantes no exe. Feito por (a): comando `fix-lists`.
- **Mixlist.bin** (272000) + Mixhelp.dat: lista de combinações para a janela nova de mistura (NewItemMix.wyt) — só client,
  mas o servidor vai precisar tratar o pacote dessa janela se for usar.
- Itemname.bin 208692; mesh/MeshTextureList.bin e UI/*.bin agora binários (só client).
- cdata.bin: não veio no rar (falhou a extração; não está nos patches) — o do 7.55 serve.

## 5. Limites (A)
- MaxHp/MaxMp: teto do client sobe de 64000 para **100000** (0x53cf0a / 0x53cf47) e vida/mana viram **int** em todos
  os pacotes (SCORE, 0x181, 0x39D CurrentMp). → tirar o `Unsigned()`/ushort de Score.Write e UpdateHpMp; limitar em 100000.
- Exp vira **int64** (MOB, SELCHAR, 0x337, 0x338, ataque) → tirar o `ClientExp` de 32 bits.
- Dano por golpe: int.
- LearnedSkill: 64 bits.

## 6. Outros (M/B)
- Opcodes novos tratados: 0x105, 0x106 (cena de campo e principal), 0x3E8 (principal). Não são obrigatórios.
- 0x20A (224) virou 0x20E (224) na tabela de tamanhos (não usado pelo servidor).
- Tick/relógio: 0x114 continua com Hold em 0x428 e o resto igual; nada indica mudança no SetTime.

## Checklist do servidor (ordem sugerida)
1. 0x20D: layout novo + versão 758 (Packets.cs, ServerOptions, KeyTableFinder, testes).
2. Structs: SCORE 48 (ints), SELCHAR 840, MOB 1024, Exp int64, LearnedSkill 64 bits.
3. 0x10A (no lugar do 0x10E), 0x110/0x112 856, 0x114 1712.
4. 0x181 28, 0x336 144 (Affect short[32]), 0x337 48, 0x338 32, 0x364 232.
5. Ataque: novo layout/tamanhos, opcodes 0x367↔0x36C trocados, Motion em 0x2E, dano int.
6. ItemList/SkillData do 7.57 com checksum acertado (e constantes no servidor: ClientChecksum 0x381AF9B, SkillData 0x184918,
   248 skills).
7. HP/MP até 100000; tirar o patch-client do 7.54 (endereços do exe mudaram).
