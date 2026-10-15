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
        await using (var command = new MySqlCommand(sql, connection))
            await command.ExecuteNonQueryAsync();

        // Colunas acrescentadas depois da primeira versão: o CREATE TABLE IF NOT EXISTS não altera
        // tabelas que já existem, então elas são adicionadas aqui (funciona em MySQL e MariaDB).
        foreach (var (table, column, definition) in Migrations)
        {
            await using var check = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c",
                connection);
            check.Parameters.AddWithValue("@t", table);
            check.Parameters.AddWithValue("@c", column);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0) continue;

            await using var alter = new MySqlCommand($"ALTER TABLE `{table}` ADD COLUMN `{column}` {definition}", connection);
            await alter.ExecuteNonQueryAsync();
            logger.LogInformation("Coluna {Table}.{Column} adicionada.", table, column);
        }

        // O nome deixou de ser único no banco: o Arch nasce com o nome do Mortal na mesma conta.
        await using (var index = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'characters' AND INDEX_NAME = 'uq_characters_name'",
            connection))
        {
            if (Convert.ToInt32(await index.ExecuteScalarAsync()) > 0)
            {
                await using var drop = new MySqlCommand(
                    "ALTER TABLE characters DROP INDEX uq_characters_name, ADD INDEX ix_characters_name (name)", connection);
                await drop.ExecuteNonQueryAsync();
                logger.LogInformation("Índice único de characters.name trocado por um índice comum (Arch com o nome do Mortal).");
            }
        }

        logger.LogInformation("Banco de dados pronto ({Database}).", connection.Database);
    }

    private static readonly (string Table, string Column, string Definition)[] Migrations =
    [
        ("accounts", "second_password_hash", "VARCHAR(255) NULL AFTER password_hash"),
        ("characters", "score_bonus", "SMALLINT NOT NULL DEFAULT 0"),
        ("characters", "special_bonus", "SMALLINT NOT NULL DEFAULT 0"),
        ("characters", "skill_bonus", "SMALLINT NOT NULL DEFAULT 0"),
        ("characters", "learned_skill", "INT UNSIGNED NOT NULL DEFAULT 0"),
        ("characters", "skill_bar", "VARBINARY(20) NULL"),
        ("characters", "special1", "TINYINT UNSIGNED NOT NULL DEFAULT 0"),
        ("characters", "special2", "TINYINT UNSIGNED NOT NULL DEFAULT 0"),
        ("characters", "special3", "TINYINT UNSIGNED NOT NULL DEFAULT 0"),
        ("characters", "special4", "TINYINT UNSIGNED NOT NULL DEFAULT 0"),
    ];
}

public sealed class MySqlAccountRepository(MySqlDatabase db) : IAccountRepository
{
    public async Task<Account?> FindByUsernameAsync(string username)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT id, username, password_hash, second_password_hash, access_level, banned, cargo_coin, cargo FROM accounts WHERE username = @u",
            connection);
        command.Parameters.AddWithValue("@u", username);

        await using var r = await command.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        return new Account
        {
            Id = r.GetInt32("id"),
            Username = r.GetString("username"),
            PasswordHash = r.GetString("password_hash"),
            SecondPasswordHash = r.IsDBNull(r.GetOrdinal("second_password_hash")) ? null : r.GetString("second_password_hash"),
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

    public async Task SetSecondPasswordAsync(int accountId, string? hash)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand(
            "UPDATE accounts SET second_password_hash = @h WHERE id = @id", connection);
        command.Parameters.AddWithValue("@h", hash);
        command.Parameters.AddWithValue("@id", accountId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveCargoAsync(Account account)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand("UPDATE accounts SET cargo = @c, cargo_coin = @coin WHERE id = @id", connection);
        command.Parameters.AddWithValue("@c", Item.ToBlob(account.Cargo));
        command.Parameters.AddWithValue("@coin", account.CargoCoin);
        command.Parameters.AddWithValue("@id", account.Id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SetAccessLevelAsync(int accountId, int level)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand("UPDATE accounts SET access_level = @l WHERE id = @id", connection);
        command.Parameters.AddWithValue("@l", level);
        command.Parameters.AddWithValue("@id", accountId);
        await command.ExecuteNonQueryAsync();
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
                ScoreBonus = r.GetInt16("score_bonus"),
                SpecialBonus = r.GetInt16("special_bonus"),
                SkillBonus = r.GetInt16("skill_bonus"),
                LearnedSkill = r.GetUInt32("learned_skill"),
                SkillBar = Character.ReadSkillBar(r.IsDBNull(r.GetOrdinal("skill_bar")) ? null : (byte[])r["skill_bar"]),
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
                Special1 = r.GetByte("special1"),
                Special2 = r.GetByte("special2"),
                Special3 = r.GetByte("special3"),
                Special4 = r.GetByte("special4"),
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

        // O mesmo nome só vale dentro da mesma conta (o Mortal e o Arch dele).
        await using (var taken = new MySqlCommand("SELECT 1 FROM characters WHERE name = @n AND account_id <> @a LIMIT 1", connection))
        {
            taken.Parameters.AddWithValue("@n", c.Name);
            taken.Parameters.AddWithValue("@a", c.AccountId);
            if (await taken.ExecuteScalarAsync() is not null) return false;
        }

        await using var command = new MySqlCommand("""
            INSERT INTO characters
                (account_id, slot, name, class, level, exp, coin, guild, pos_x, pos_y,
                 ac, damage, max_hp, max_mp, hp, mp, str, `int`, dex, con, equip, inventory,
                 score_bonus, special_bonus, skill_bonus, learned_skill)
            VALUES
                (@account, @slot, @name, @class, @level, @exp, @coin, @guild, @x, @y,
                 @ac, @damage, @maxhp, @maxmp, @hp, @mp, @str, @int, @dex, @con, @equip, @inventory,
                 @sb, @spb, @skb, @ls)
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
        command.Parameters.AddWithValue("@sb", c.ScoreBonus);           // o Arch nasce com 528 pontos
        command.Parameters.AddWithValue("@spb", c.SpecialBonus);
        command.Parameters.AddWithValue("@skb", c.SkillBonus);
        command.Parameters.AddWithValue("@ls", c.LearnedSkill);

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

    public async Task SavePositionAsync(int characterId, short x, short y)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand(
            "UPDATE characters SET pos_x = @x, pos_y = @y WHERE id = @id", connection);
        command.Parameters.AddWithValue("@x", x);
        command.Parameters.AddWithValue("@y", y);
        command.Parameters.AddWithValue("@id", characterId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveInventoryAsync(int characterId, Item[] inventory)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand("UPDATE characters SET inventory = @inv WHERE id = @id", connection);
        command.Parameters.AddWithValue("@inv", Item.ToBlob(inventory));
        command.Parameters.AddWithValue("@id", characterId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveItemsAsync(Character c)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand(
            "UPDATE characters SET coin = @coin, equip = @equip, inventory = @inv, hp = @hp, mp = @mp WHERE id = @id", connection);
        command.Parameters.AddWithValue("@coin", c.Coin);
        command.Parameters.AddWithValue("@equip", Item.ToBlob(c.Equip));
        command.Parameters.AddWithValue("@inv", Item.ToBlob(c.Inventory));
        command.Parameters.AddWithValue("@hp", c.BaseScore.Hp);
        command.Parameters.AddWithValue("@mp", c.BaseScore.Mp);
        command.Parameters.AddWithValue("@id", c.Id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveProgressAsync(Character c)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand("""
            UPDATE characters SET exp = @exp, level = @level, coin = @coin, max_hp = @maxhp, max_mp = @maxmp,
                hp = @hp, mp = @mp, score_bonus = @sb, special_bonus = @spb, skill_bonus = @skb, learned_skill = @ls,
                skill_bar = @bar, str = @str, `int` = @int, dex = @dex, con = @con,
                special1 = @s1, special2 = @s2, special3 = @s3, special4 = @s4
            WHERE id = @id
            """, connection);
        var s = c.BaseScore;
        command.Parameters.AddWithValue("@exp", c.Exp);
        command.Parameters.AddWithValue("@level", s.Level);
        command.Parameters.AddWithValue("@coin", c.Coin);
        command.Parameters.AddWithValue("@maxhp", s.MaxHp);
        command.Parameters.AddWithValue("@maxmp", s.MaxMp);
        command.Parameters.AddWithValue("@hp", s.Hp);
        command.Parameters.AddWithValue("@mp", s.Mp);
        command.Parameters.AddWithValue("@sb", c.ScoreBonus);
        command.Parameters.AddWithValue("@spb", c.SpecialBonus);
        command.Parameters.AddWithValue("@skb", c.SkillBonus);
        command.Parameters.AddWithValue("@ls", c.LearnedSkill);
        command.Parameters.AddWithValue("@bar", c.SkillBar);
        command.Parameters.AddWithValue("@str", s.Str);
        command.Parameters.AddWithValue("@int", s.Int);
        command.Parameters.AddWithValue("@dex", s.Dex);
        command.Parameters.AddWithValue("@con", s.Con);
        command.Parameters.AddWithValue("@s1", s.Special1);
        command.Parameters.AddWithValue("@s2", s.Special2);
        command.Parameters.AddWithValue("@s3", s.Special3);
        command.Parameters.AddWithValue("@s4", s.Special4);
        command.Parameters.AddWithValue("@id", c.Id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveExpAsync(int characterId, long exp)
    {
        await using var connection = await db.OpenAsync();
        await using var command = new MySqlCommand("UPDATE characters SET exp = @exp WHERE id = @id", connection);
        command.Parameters.AddWithValue("@exp", exp);
        command.Parameters.AddWithValue("@id", characterId);
        await command.ExecuteNonQueryAsync();
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
