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
            case ["extract-exptable", var exe]:
                return ToolCommands.ExtractExpTableCommand(exe);
            case ["patch-client", var exe]:
                return ToolCommands.PatchClientCommand(exe);
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
            default:
                Console.Error.WriteLine("""
                    Uso:
                      WYD.Server                                    inicia o servidor
                      WYD.Server create-account <conta> <senha>     cria uma conta
                      WYD.Server reset-second-password <conta>      apaga a segunda senha (o próximo login define outra)
                      WYD.Server set-access <conta> <nivel>         0 = jogador, 1 = GM (libera /item no jogo)
                      WYD.Server unstuck <conta>                    leva os personagens da conta para a cidade inicial
                      WYD.Server serverlist <serverlist.bin> [ip]   mostra (ou troca) os IPs do serverlist
                      WYD.Server items <ItemList.bin> [texto]       lista os itens do jogo
                      WYD.Server extract-exptable <WYD.exe>         copia a tabela de experiência para data/ExpTable.txt
                      WYD.Server patch-client <WYD.exe>             corrige o efeito das armas +13 a +15 no client
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
