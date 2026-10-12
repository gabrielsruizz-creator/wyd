# Pasta `data/`

Arquivos que vêm do **client do jogo** ficam aqui. Eles não vão para o repositório
(estão no `.gitignore`), porque pertencem ao jogo original.

## `KeyTable.bin` (obrigatório)

Tabela de **512 bytes** que o client usa para cifrar os pacotes (`g_pKeyTable` no servidor
original). Sem ela o servidor não consegue ler nada do que o client envia.

Ela está dentro do executável do client. O próprio servidor tem um comando que a encontra sozinho:

```powershell
dotnet run --project src\WYD.Server -- find-keytable "C:\caminho\do\WYD.exe"
```

Ele espera o client conectar (o `serverlist` precisa apontar para este PC, veja o README principal),
captura o pacote de login e procura no `WYD.exe` o trecho de 512 bytes que decifra esse pacote.
Se pedir, tente logar de novo no client: às vezes um login só não basta para distinguir a tabela certa.

Se o executável estiver protegido (compactado), a tabela não aparece no arquivo. O comando então
explica como gerar um dump de memória do client pelo Gerenciador de Tarefas e procurar nele.

### Como saber se a tabela está certa

1. Ligue `"LogPackets": true` no `appsettings.json`.
2. Abra o client e tente logar.
3. No log do servidor, o pacote `RECV AccountLogin (116 bytes)` deve mostrar a sua conta e a
   senha legíveis na coluna de texto à direita. Se aparecer lixo, a tabela está errada
   (ou o client é de outra versão).

A pasta `data/` é copiada para junto do executável ao compilar, então
`dotnet run --project src/WYD.Server` encontra o arquivo automaticamente.
