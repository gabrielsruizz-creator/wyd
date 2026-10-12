using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using WYD.Server.Config;
using WYD.Server.Game;
using WYD.Server.Protocol;

namespace WYD.Server.Database;

public sealed class MySqlDatabase(IOptions<DatabaseOptions> options, ILogger<MySqlDatabase> logger)
{
    private readonly string _connectionString = options.Value.ConnectionString;

    public async Task<MySqlConnection> OpenAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Cria as tabelas que ainda não existem (sql/schema.sql embutido no executável).</summary>
    public async Task EnsureSchemaAsync()
    {
        await using var stream = typeof(MySqlDatabase).Assembly.GetManifestResourceStream("schema.sql")
            ?? throw new InvalidOperationException("schema.sql não foi embutido no executável.");
        using var reader = new StreamReader(stream);
        string sql = await reader.ReadToEndAsync();

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
        logger.LogInformation("Banco de dados pronto ({Database}).", connection.Database);
    }
}

public sealed class MySqlAccountRepository(MySqlDatabase db) : IAccountRepository
{
    public async Task<Account?> FindByUsernameAsync(string username)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT id, username, password_hash, access_level, banned, cargo_coin, cargo FROM accounts WHERE username = @u",
            connection);
        command.Parameters.AddWithValue("@u", username);

        await using var r = await command.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        return new Account
        {
            Id = r.GetInt32("id"),
            Username = r.GetString("username"),
            PasswordHash = r.GetString("password_hash"),
            AccessLevel = r.GetInt32("access_level"),
            Banned = r.GetBoolean("banned"),
            CargoCoin = r.GetInt32("cargo_coin"),
            Cargo = Item.FromBlob(r.IsDBNull(r.GetOrdinal("cargo")) ? null : (byte[])r["cargo"], GameConstants.MaxCargo),
        };
    }

    public async Task<Account> CreateAsync(string username, string passwordHash)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand(
            "INSERT INTO accounts (username, password_hash) VALUES (@u, @p)", connection);
        command.Parameters.AddWithValue("@u", username);
        command.Parameters.AddWithValue("@p", passwordHash);
        await command.ExecuteNonQueryAsync();

        return new Account { Id = (int)command.LastInsertedId, Username = username, PasswordHash = passwordHash };
    }

    public async Task UpdateLastLoginAsync(int accountId, string? ip)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand(
            "UPDATE accounts SET last_login_at = UTC_TIMESTAMP(), last_login_ip = @ip WHERE id = @id", connection);
        command.Parameters.AddWithValue("@ip", ip);
        command.Parameters.AddWithValue("@id", accountId);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class MySqlCharacterRepository(MySqlDatabase db) : ICharacterRepository
{
    public async Task<List<Character>> ListByAccountAsync(int accountId)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT * FROM characters WHERE account_id = @a ORDER BY slot", connection);
        command.Parameters.AddWithValue("@a", accountId);

        var list = new List<Character>();
        await using var r = await command.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var c = new Character
            {
                Id = r.GetInt32("id"),
                AccountId = r.GetInt32("account_id"),
                Slot = r.GetInt32("slot"),
                Name = r.GetString("name"),
                Class = (CharacterClass)r.GetByte("class"),
                Exp = r.GetInt64("exp"),
                Coin = r.GetInt32("coin"),
                Guild = r.GetUInt16("guild"),
                PosX = r.GetInt16("pos_x"),
                PosY = r.GetInt16("pos_y"),
                Equip = Item.FromBlob(r.IsDBNull(r.GetOrdinal("equip")) ? null : (byte[])r["equip"], GameConstants.MaxEquip),
                Inventory = Item.FromBlob(r.IsDBNull(r.GetOrdinal("inventory")) ? null : (byte[])r["inventory"], GameConstants.MaxCarry),
            };
            c.BaseScore = new Score
            {
                Level = r.GetInt32("level"),
                Ac = r.GetInt32("ac"),
                Damage = r.GetInt32("damage"),
                AttackRun = Score.DefaultAttackRun,
                MaxHp = r.GetInt32("max_hp"),
                MaxMp = r.GetInt32("max_mp"),
                Hp = r.GetInt32("hp"),
                Mp = r.GetInt32("mp"),
                Str = r.GetInt16("str"),
                Int = r.GetInt16("int"),
                Dex = r.GetInt16("dex"),
                Con = r.GetInt16("con"),
            };
            list.Add(c);
        }
        return list;
    }

    public async Task<bool> NameExistsAsync(string name)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand("SELECT 1 FROM characters WHERE name = @n LIMIT 1", connection);
        command.Parameters.AddWithValue("@n", name);
        return await command.ExecuteScalarAsync() is not null;
    }

    public async Task<bool> CreateAsync(Character c)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand("""
            INSERT INTO characters
                (account_id, slot, name, class, level, exp, coin, guild, pos_x, pos_y,
                 ac, damage, max_hp, max_mp, hp, mp, str, `int`, dex, con, equip, inventory)
            VALUES
                (@account, @slot, @name, @class, @level, @exp, @coin, @guild, @x, @y,
                 @ac, @damage, @maxhp, @maxmp, @hp, @mp, @str, @int, @dex, @con, @equip, @inventory)
            """, connection);

        var s = c.BaseScore;
        command.Parameters.AddWithValue("@account", c.AccountId);
        command.Parameters.AddWithValue("@slot", c.Slot);
        command.Parameters.AddWithValue("@name", c.Name);
        command.Parameters.AddWithValue("@class", (byte)c.Class);
        command.Parameters.AddWithValue("@level", s.Level);
        command.Parameters.AddWithValue("@exp", c.Exp);
        command.Parameters.AddWithValue("@coin", c.Coin);
        command.Parameters.AddWithValue("@guild", c.Guild);
        command.Parameters.AddWithValue("@x", c.PosX);
        command.Parameters.AddWithValue("@y", c.PosY);
        command.Parameters.AddWithValue("@ac", s.Ac);
        command.Parameters.AddWithValue("@damage", s.Damage);
        command.Parameters.AddWithValue("@maxhp", s.MaxHp);
        command.Parameters.AddWithValue("@maxmp", s.MaxMp);
        command.Parameters.AddWithValue("@hp", s.Hp);
        command.Parameters.AddWithValue("@mp", s.Mp);
        command.Parameters.AddWithValue("@str", s.Str);
        command.Parameters.AddWithValue("@int", s.Int);
        command.Parameters.AddWithValue("@dex", s.Dex);
        command.Parameters.AddWithValue("@con", s.Con);
        command.Parameters.AddWithValue("@equip", Item.ToBlob(c.Equip));
        command.Parameters.AddWithValue("@inventory", Item.ToBlob(c.Inventory));

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }

        c.Id = (int)command.LastInsertedId;
        return true;
    }

    public async Task<bool> DeleteAsync(int accountId, int slot)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand(
            "DELETE FROM characters WHERE account_id = @a AND slot = @s", connection);
        command.Parameters.AddWithValue("@a", accountId);
        command.Parameters.AddWithValue("@s", slot);
        return await command.ExecuteNonQueryAsync() > 0;
    }
}
