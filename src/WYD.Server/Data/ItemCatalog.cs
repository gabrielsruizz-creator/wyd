using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;

namespace WYD.Server.Data;

/// <summary>O ItemList.bin carregado no servidor, se o arquivo existir (é opcional).</summary>
public sealed class ItemCatalog
{
    public ItemCatalog(IOptions<ServerOptions> options, ILogger<ItemCatalog> logger)
    {
        string path = PathResolver.Resolve(options.Value.ItemListPath);
        if (!File.Exists(path))
        {
            logger.LogInformation("Sem ItemList.bin em {Path}: os itens criados por GMs não serão conferidos.", path);
            return;
        }

        try
        {
            List = ItemList.Load(path, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "ExtraItem.bin"));
            logger.LogInformation("ItemList.bin carregado: {Count} itens.", List.All.Count());
        }
        catch (InvalidDataException ex)
        {
            logger.LogWarning("ItemList.bin inválido: {Message}", ex.Message);
        }
    }

    public ItemCatalog(ItemList? list) => List = list;

    public ItemList? List { get; }
}
