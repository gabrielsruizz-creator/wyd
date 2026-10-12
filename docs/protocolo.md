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
- ✅ validado nos testes automáticos: o formato bate entre o servidor e o client de teste;
- ⚠️ a validar: o layout segue a documentação da comunidade, mas ainda precisa ser conferido
  com o client real (ligue `LogPackets`).

### Client → Servidor

| Opcode  | Nome               | Tamanho | Conteúdo                                                                 | Situação |
|---------|--------------------|--------:|--------------------------------------------------------------------------|----------|
| `0x20D` | AccountLogin       | 116     | `Account[16]`, `Password[12]`, `int Version`, `int DBNeedSave`, `int Adapter[4]`, `Zero[52]` | ✅ client 7.54 |
| `0x20F` | CreateCharacter    | 36      | `int Slot`, `Name[16]`, `int Class`                                      | ⚠️ |
| `0x211` | DeleteCharacter    | 44      | `int Slot`, `Name[16]`, `Password[12]`                                   | ⚠️ |
| `0x213` | CharacterLogin     | —       | `int Slot`, …                                                            | ⚠️ ainda não implementado |
| `0x3A0` | Ping               | —       | ignorado                                                                 | ⚠️ |

### Servidor → Client

| Opcode  | Nome                | Tamanho | Conteúdo                                                                   | Situação |
|---------|---------------------|--------:|----------------------------------------------------------------------------|----------|
| `0x101` | MessagePanel        | 140     | `char Text[128]`                                                           | ⚠️ |
| `0x10A` | AccountLoginOk      | 1928    | `HashKeyTable[16]`, `SELCHAR`, `ITEM Cargo[128]`, `int Coin`, `Account[16]`, `16 bytes` | ⚠️ |
| `0x110` | CreateCharacterOk   | 852     | `SELCHAR`                                                                  | ⚠️ |
| `0x112` | DeleteCharacterOk   | 852     | `SELCHAR`                                                                  | ⚠️ |
| `0x11A` | CreateCharacterFail | 16      | `int Parm`                                                                 | ⚠️ |
| `0x11B` | DeleteCharacterFail | 16      | `int Parm`                                                                 | ⚠️ |

### Structs

- **`STRUCT_ITEM`** (8 bytes): `short Index`, depois `{byte Effect, byte Value}` × 3.
- **`STRUCT_SCORE`** (48 bytes): `int Level, Ac, Damage`, `byte Merchant, AttackRun, Direction, ChaosRate`,
  `int MaxHp, MaxMp, Hp, Mp`, `short Str, Int, Dex, Con`, `ushort Special[4]`.
- **`STRUCT_SELCHAR`** (840 bytes, os 4 slots da conta): `short SPX[4]`, `short SPY[4]`, `char Name[4][16]`,
  `SCORE Score[4]`, `ITEM Equip[4][16]`, `ushort Guild[4]`, `int Coin[4]`, `long long Exp[4]`.
  `Equip[0]` é o "rosto" do personagem (1 = TK, 11 = FM, 21 = BM, 31 = HT).

## Como validar um layout com o client real

1. Ligue `"LogPackets": true` e conecte com o client.
2. Para pacotes **recebidos**: compare o hex do log com a tabela acima (offsets contados a partir do
   início do pacote, cabeçalho incluso).
3. Para pacotes **enviados**: se o client travar, fechar ou mostrar dados embaralhados, o tamanho ou a
   ordem dos campos está diferente. Os layouts ficam todos em
   [`Packets.cs`](../src/WYD.Server/Protocol/Packets.cs), e o `PacketWriter` se recusa a montar um
   pacote cujo tamanho não bata, então os erros aparecem logo.
4. Quando confirmar um pacote, troque ⚠️ por ✅ aqui.
