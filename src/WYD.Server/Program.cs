using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;
using WYD.Server.Database;
using WYD.Server.Handlers;
using WYD.Server.Network;
using WYD.Server.Security;
using WYD.Server.Tools;

namespace WYD.Server;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Ferramentas que não usam o banco.
        switch (args)
        {
            case ["serverlist", var path]:
                return ToolCommands.ServerListCommand(path, null);
            case ["serverlist", var path, var ip]:
                return ToolCommands.ServerListCommand(path, ip);
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
            default:
                Console.Error.WriteLine("""
                    Uso:
                      WYD.Server                                    inicia o servidor
                      WYD.Server create-account <conta> <senha>     cria uma conta
                      WYD.Server serverlist <serverlist.bin> [ip]   mostra (ou troca) os IPs do serverlist
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

        services.AddSingleton(sp => KeyTable.Load(ResolvePath(sp.GetRequiredService<IOptions<ServerOptions>>().Value.KeyTablePath)));
        services.AddSingleton(sp => new SessionManager(sp.GetRequiredService<IOptions<ServerOptions>>().Value.MaxUsers));

        services.AddSingleton<MySqlDatabase>();
        services.AddSingleton<IAccountRepository, MySqlAccountRepository>();
        services.AddSingleton<ICharacterRepository, MySqlCharacterRepository>();

        services.AddSingleton<LoginHandler>();
        services.AddSingleton<CharacterHandler>();
        services.AddSingleton<PacketDispatcher>();

        services.AddSingleton<GameServer>();
        services.AddHostedService(sp => sp.GetRequiredService<GameServer>());
        services.AddHostedService<StatusService>();
        return services;
    }

    private static string? OptionValue(string[] options, string name)
    {
        int i = Array.IndexOf(options, name);
        return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
    }

    /// <summary>Caminhos relativos valem a partir da pasta atual ou, se não existirem lá, da pasta do executável.</summary>
    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path) || File.Exists(path)) return path;
        string beside = Path.Combine(AppContext.BaseDirectory, path);
        return File.Exists(beside) ? beside : path;
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
