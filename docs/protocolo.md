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
| `0x3A0` | Ping               | —       | ignorado                                                                 | ⚠️ |

### Servidor → Client

| Opcode  | Nome                | Tamanho | Conteúdo                                                                   | Situação |
|---------|---------------------|--------:|----------------------------------------------------------------------------|----------|
| `0x101` | MessagePanel        | 108     | `char Text[96]`                                                            | ✅ |
| `0x10E` | AccountLoginOk      | 1816    | `SELCHAR` (0x00C), `ITEM Cargo[128]` (0x2F4), `int Coin` (0x6F4), `Account[16]` (0x6F8), `Keys[16]` (0x708) | ✅ |
| `0x110` | CreateCharacterOk   | 756     | `SELCHAR`                                                                  | ✅ |
| `0x112` | DeleteCharacterOk   | 756     | `SELCHAR`                                                                  | ✅ |
| `0x114` | CharacterLoginOk    | 800     | `short X` (0x0C), `short Y` (0x0E), `STRUCT_MOB` (0x10), `ushort IdDoJogador` (0x306), `ushort Clima` (0x308), `byte SkillBar2[16]` (0x30A), `int ?` (0x31C) | ✅ offsets; ⚠️ falta testar no client |
| `0x119` | CharacterLoginFail  | 16      | o client mostra a mensagem dele                                            | ✅ |
| `0x11A` | CreateCharacterFail | 16      | o client mostra a mensagem dele                                            | ✅ |
| `0x11B` | DeleteCharacterFail | 16      | o client mostra a mensagem dele                                            | ✅ |

> Atenção: no 7.54 o login aceito é o `0x10E`. O `0x10A` que aparece em documentação de outras
> versões é ignorado por este client.

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
