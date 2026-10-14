using WYD.Server.Game;
using WYD.Server.Protocol;

namespace WYD.Server.Database;

public interface IAccountRepository
{
    Task<Account?> FindByUsernameAsync(string username);
    Task<Account> CreateAsync(string username, string passwordHash);
    Task UpdateLastLoginAsync(int accountId, string? ip);

    /// <summary>Grava (ou apaga, com null) o hash da segunda senha.</summary>
    Task SetSecondPasswordAsync(int accountId, string? hash);

    /// <summary>0 = jogador; 1 ou mais = GM (pode usar comandos como /item).</summary>
    Task SetAccessLevelAsync(int accountId, int level);
}

public interface ICharacterRepository
{
    Task<List<Character>> ListByAccountAsync(int accountId);
    Task<bool> NameExistsAsync(string name);

    /// <summary>Insere o personagem. Retorna false se o nome ou o slot já estão em uso.</summary>
    Task<bool> CreateAsync(Character character);

    Task<bool> DeleteAsync(int accountId, int slot);

    Task SavePositionAsync(int characterId, short x, short y);

    Task SaveInventoryAsync(int characterId, Item[] inventory);
}
