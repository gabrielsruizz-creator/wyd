# Pasta `data/`

Arquivos que vêm do **client do jogo** ficam aqui. Eles não vão para o repositório
(estão no `.gitignore`), porque pertencem ao jogo original.

## `KeyTable.bin` (obrigatório)

Tabela de **512 bytes** que o client usa para cifrar os pacotes (`g_pKeyTable` no servidor
original). Sem ela o servidor não consegue ler nada do que o client envia.

Ela está dentro do executável do client da versão que você vai usar. Você pode:

- copiar a tabela de arquivos de servidor que você já tenha da mesma versão, ou
- extraí-la do executável do seu client com um editor hexadecimal e salvar os 512 bytes
  como `data/KeyTable.bin`.

### Como saber se a tabela está certa

1. Ligue `"LogPackets": true` no `appsettings.json`.
2. Abra o client e tente logar.
3. No log do servidor, o pacote `RECV AccountLogin (116 bytes)` deve mostrar a sua conta e a
   senha legíveis na coluna de texto à direita. Se aparecer lixo, a tabela está errada
   (ou o client é de outra versão).

A pasta `data/` é copiada para junto do executável ao compilar, então
`dotnet run --project src/WYD.Server` encontra o arquivo automaticamente.
