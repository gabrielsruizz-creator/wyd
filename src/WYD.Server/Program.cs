using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;
using WYD.Server.Data;
using WYD.Server.Database;
using WYD.Server.Game;
using WYD.Server.Handlers;
using WYD.Server.Network;
using WYD.Server.Security;
using WYD.Server.Tools;

namespace WYD.Server;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Sem isso o console do Windows mostra "Vers├úo" no lugar de "Versão".
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Ferramentas que não usam o banco.
        switch (args)
        {
            case ["serverlist", var path]:
                return ToolCommands.ServerListCommand(path, null);
            case ["serverlist", var path, var ip]:
                return ToolCommands.ServerListCommand(path, ip);
            case ["items", var itemList]:
                return ToolCommands.ItemsCommand(itemList, null);
            case ["items", var itemList, var filter]:
                return ToolCommands.ItemsCommand(itemList, filter);
            case ["launcher", var client, var ip]:
                return ToolCommands.LauncherCommand(client, ip);
            case ["client-config", var client, .. var options]:
                return ToolCommands.ClientConfigCommand(client, options);
            case ["fix-lists", var client]:
                return ToolCommands.FixListsCommand(client);
            case ["extract-exptable", var exe]:
                return ToolCommands.ExtractExpTableCommand(exe);
            case ["patch-client", var exe]:
                return ToolCommands.PatchClientCommand(exe);
            case ["publish-client", var exe]:
                return ToolCommands.PublishClientCommand(exe);
            case ["find-keytable", var clientFile, .. var options]:
                return await ToolCommands.FindKeyTableAsync(
                    clientFile, int.Parse(OptionValue(options, "--port") ?? "8281"), OptionValue(options, "--status"));
        }

        // Comandos de administração: dotnet run -- create-account <conta> <senha>
        string[] hostArgs = args.SkipWhile(a => !a.StartsWith('-')).ToArray();
        string? command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null;

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = hostArgs,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Services.AddWydServer(builder.Configuration);
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = false;
            o.TimestampFormat = "HH:mm:ss ";
        });

        using var host = builder.Build();
        var db = host.Services.GetRequiredService<MySqlDatabase>();
        var logger = host.Services.GetRequiredService<ILogger<GameServer>>();

        try
        {
            await db.EnsureSchemaAsync();
        }
        catch (Exception ex)
        {
            logger.LogCritical("Não foi possível conectar ao MySQL: {Message}. Confira Database:ConnectionString no appsettings.json.", ex.Message);
            return 1;
        }

        switch (command)
        {
            case null:
                break;
            case "create-account" when args.Length == 3:
                return await CreateAccountAsync(host.Services, args[1], args[2]);
            case "reset-second-password" when args.Length == 2:
                return await ResetSecondPasswordAsync(host.Services, args[1]);
            case "set-access" when args.Length == 3 && int.TryParse(args[2], out int level):
                return await SetAccessAsync(host.Services, args[1], level);
            case "unstuck" when args.Length == 2:
                return await UnstuckAsync(host.Services, args[1]);
            case "unequip" when args.Length >= 3:
                return await UnequipAsync(host.Services, args[1], args[2], args[3..]);
            case "show" when args.Length == 3:
                return await ShowAsync(host.Services, args[1], args[2]);
            default:
                Console.Error.WriteLine("""
                    Uso:
                      WYD.Server                                    inicia o servidor
                      WYD.Server create-account <conta> <senha>     cria uma conta
                      WYD.Server reset-second-password <conta>      apaga a segunda senha (o próximo login define outra)
                      WYD.Server set-access <conta> <nivel>         0 = jogador, 1 = GM (libera /item no jogo)
                      WYD.Server unstuck <conta>                    leva os personagens da conta para a cidade inicial
                      WYD.Server unequip <conta> <personagem> [slot...]  apaga peças equipadas (sem slot: as armas, 6 e 7)
                      WYD.Server show <conta> <personagem>          mostra a ficha, o equipamento e o inventário
                      WYD.Server serverlist <serverlist.bin> [ip]   mostra (ou troca) os IPs do serverlist
                      WYD.Server items <ItemList.bin> [texto]       lista os itens do jogo
                      WYD.Server launcher <pasta do client> <ip>     aponta o launcher (Settings.txt) para o servidor
                      WYD.Server client-config <pasta do client> [janela|tela-cheia] [1024x768]  modo de tela e resolução (Config.bin)
                      WYD.Server fix-lists <pasta do client>         acerta o checksum da ItemList e da SkillData e copia para data/
                      WYD.Server extract-exptable <WYD.exe>         copia a tabela de experiência para data/ExpTable.txt
                      WYD.Server patch-client <WYD.exe>             corrige o client: efeito das armas +13 a +15, item em 1 slot
                      WYD.Server publish-client <WYD.exe>           põe o WYD.exe corrigido em data/patch para o launcher baixar
                      WYD.Server find-keytable <WYD.exe|.DMP> [--port 8281] [--status "texto"]  acha a KeyTable do client
                    """);
                return 2;
        }

        try
        {
            host.Services.GetRequiredService<KeyTable>();
        }
        catch (FileNotFoundException ex)
        {
            logger.LogCritical("{Message}", ex.Message);
            return 1;
        }

        await host.RunAsync();
        return 0;
    }

    public static IServiceCollection AddWydServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ServerOptions>(configuration.GetSection(ServerOptions.Section));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));

        services.AddSingleton(sp => KeyTable.Load(PathResolver.Resolve(sp.GetRequiredService<IOptions<ServerOptions>>().Value.KeyTablePath)));
        services.AddSingleton(sp => new SessionManager(sp.GetRequiredService<IOptions<ServerOptions>>().Value.MaxUsers));

        services.AddSingleton<MySqlDatabase>();
        services.AddSingleton<IAccountRepository, MySqlAccountRepository>();
        services.AddSingleton<ICharacterRepository, MySqlCharacterRepository>();

        services.AddSingleton<ItemCatalog>();
        services.AddSingleton<ExpCatalog>();
        services.AddSingleton<SkillCatalog>();
        services.AddSingleton<CommandHandler>();
        services.AddSingleton<ItemHandler>();
        services.AddSingleton<PointsHandler>();
        services.AddSingleton<KingHandler>();
        services.AddSingleton<KingdomPoints>();
        services.AddSingleton<TeleportTable>();
        services.AddSingleton<NpcVisuals>();
        services.AddSingleton<TeleportHandler>();
        services.AddSingleton<World>();
        services.AddSingleton<WorldHandler>();
        services.AddSingleton<LoginHandler>();
        services.AddSingleton<CharacterHandler>();
        services.AddSingleton<PacketDispatcher>();

        services.AddSingleton<GameServer>();
        services.AddHostedService(sp => sp.GetRequiredService<GameServer>());
        services.AddHostedService<StatusService>();
        services.AddSingleton<MobService>();
        services.AddHostedService(sp => sp.GetRequiredService<MobService>());
        return services;
    }

    private static string? OptionValue(string[] options, string name)
    {
        int i = Array.IndexOf(options, name);
        return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
    }

    private static async Task<int> SetAccessAsync(IServiceProvider services, string username, int level)
    {
        var accounts = services.GetRequiredService<IAccountRepository>();
        if (await accounts.FindByUsernameAsync(username) is not { } account)
        {
            Console.Error.WriteLine($"A conta '{username}' não existe.");
            return 1;
        }

        await accounts.SetAccessLevelAsync(account.Id, level);
        Console.WriteLine($"Conta '{username}' agora tem nível de acesso {level}{(level > 0 ? " (GM)" : "")}. Vale a partir do próximo login.");
        return 0;
    }

    /// <summary>unstuck: personagem preso num lugar que derruba o client volta para StartX/StartY.</summary>
    private static async Task<int> UnstuckAsync(IServiceProvider services, string username)
    {
        var accounts = services.GetRequiredService<IAccountRepository>();
        if (await accounts.FindByUsernameAsync(username) is not { } account)
        {
            Console.Error.WriteLine($"A conta '{username}' não existe.");
            return 1;
        }

        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServerOptions>>().Value;
        var characters = services.GetRequiredService<ICharacterRepository>();
        foreach (var c in await characters.ListByAccountAsync(account.Id))
        {
            await characters.SavePositionAsync(c.Id, (short)options.StartX, (short)options.StartY);
            Console.WriteLine($"{c.Name}: de ({c.PosX}, {c.PosY}) para ({options.StartX}, {options.StartY}).");
        }
        Console.WriteLine("Pronto. Se o servidor estiver aberto, o jogador precisa estar fora do jogo.");
        return 0;
    }

    /// <summary>
    /// unequip &lt;conta&gt; &lt;personagem&gt; [slot...]: apaga peças equipadas de um personagem que não consegue entrar
    /// (por exemplo uma arma sem modelo no client, que o fecha ao desenhar o personagem). Sem slot, as duas armas.
    /// </summary>
    private static async Task<int> UnequipAsync(IServiceProvider services, string username, string name, string[] slotArgs)
    {
        var slots = slotArgs.Length == 0 ? [6, 7] : slotArgs.Select(s => int.TryParse(s, out int n) ? n : -1).ToArray();
        if (slots.Any(s => s is < 0 or >= Protocol.GameConstants.MaxEquip))
        {
            Console.Error.WriteLine($"Slots vão de 0 a {Protocol.GameConstants.MaxEquip - 1} (6 = mão direita, 7 = mão esquerda, 15 = capa).");
            return 1;
        }
        var accounts = services.GetRequiredService<IAccountRepository>();
        if (await accounts.FindByUsernameAsync(username) is not { } account)
        {
            Console.Error.WriteLine($"A conta '{username}' não existe.");
            return 1;
        }
        var characters = services.GetRequiredService<ICharacterRepository>();
        var list = await characters.ListByAccountAsync(account.Id);
        var c = list.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (c is null)
        {
            Console.Error.WriteLine($"A conta '{username}' não tem o personagem '{name}' (tem: {string.Join(", ", list.Select(x => x.Name))}).");
            return 1;
        }
        foreach (int slot in slots)
        {
            Console.WriteLine(c.Equip[slot].IsEmpty ? $"Slot {slot}: já estava vazio." : $"Slot {slot}: item {c.Equip[slot].Index} apagado.");
            c.Equip[slot] = default;
        }
        await characters.SaveItemsAsync(c);
        Console.WriteLine("Pronto. Se o servidor estiver aberto, o jogador precisa estar fora do jogo.");
        return 0;
    }

    private static async Task<int> ShowAsync(IServiceProvider services, string username, string name)
    {
        var accounts = services.GetRequiredService<IAccountRepository>();
        if (await accounts.FindByUsernameAsync(username) is not { } account)
        {
            Console.Error.WriteLine($"A conta '{username}' não existe.");
            return 1;
        }
        var list = await services.GetRequiredService<ICharacterRepository>().ListByAccountAsync(account.Id);
        var c = list.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (c is null)
        {
            Console.Error.WriteLine($"A conta '{username}' não tem o personagem '{name}' (tem: {string.Join(", ", list.Select(x => x.Name))}).");
            return 1;
        }
        var items = services.GetRequiredService<Data.ItemCatalog>().List;
        string Describe(Protocol.Item i) => i.IsEmpty ? "-" :
            $"{i.Index} {items?[i.Index]?.Name ?? "(não existe no ItemList)"} [{i.Effect1}:{i.Value1} {i.Effect2}:{i.Value2} {i.Effect3}:{i.Value3}]";
        var s = c.BaseScore;
        Console.WriteLine($"{c.Name} (slot {c.Slot}): {c.Class}, {c.Evolution}, nível {s.Level + 1}, exp {c.Exp}, gold {c.Coin}, em ({c.PosX}, {c.PosY})");
        Console.WriteLine($"  FOR {s.Str} INT {s.Int} DES {s.Dex} CON {s.Con}, vida {s.Hp}/{s.MaxHp}, mana {s.Mp}/{s.MaxMp}, especiais {s.Special1}/{s.Special2}/{s.Special3}/{s.Special4}");
        Console.WriteLine("  Equipamento:");
        for (int i = 0; i < c.Equip.Length; i++) Console.WriteLine($"    {i,2}: {Describe(c.Equip[i])}");
        Console.WriteLine("  Inventário:");
        for (int i = 0; i < c.Inventory.Length; i++)
            if (!c.Inventory[i].IsEmpty) Console.WriteLine($"    {i,2}: {Describe(c.Inventory[i])}");
        return 0;
    }

    private static async Task<int> ResetSecondPasswordAsync(IServiceProvider services, string username)
    {
        var accounts = services.GetRequiredService<IAccountRepository>();
        if (await accounts.FindByUsernameAsync(username) is not { } account)
        {
            Console.Error.WriteLine($"A conta '{username}' não existe.");
            return 1;
        }

        await accounts.SetSecondPasswordAsync(account.Id, null);
        Console.WriteLine($"Segunda senha de '{username}' apagada. A próxima senha digitada no jogo passa a ser a nova.");
        return 0;
    }

    private static async Task<int> CreateAccountAsync(IServiceProvider services, string username, string password)
    {
        if (!LoginHandler.UsernamePattern().IsMatch(username))
        {
            Console.Error.WriteLine("A conta deve ter de 4 a 15 letras ou números.");
            return 2;
        }
        if (password.Length is < 4 or > 11)
        {
            Console.Error.WriteLine("A senha deve ter de 4 a 11 caracteres.");
            return 2;
        }

        var accounts = services.GetRequiredService<IAccountRepository>();
        if (await accounts.FindByUsernameAsync(username) is not null)
        {
            Console.Error.WriteLine($"A conta '{username}' já existe.");
            return 1;
        }

        await accounts.CreateAsync(username, PasswordHasher.Hash(password));
        Console.WriteLine($"Conta '{username}' criada.");
        return 0;
    }
}
