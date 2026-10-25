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

    Task SaveCargoAsync(Account account);

    /// <summary>0 = jogador; 1 ou mais = GM (pode usar comandos como /item).</summary>
    Task SetAccessLevelAsync(int accountId, int level);

    Task SetPasswordAsync(int accountId, string passwordHash);

    /// <summary>Todas as contas, por nome (sem o baú).</summary>
    Task<List<(Account Account, DateTime? LastLogin)>> ListAsync();
}

public interface ICharacterRepository
{
    Task<List<Character>> ListByAccountAsync(int accountId);
    Task<bool> NameExistsAsync(string name);

    /// <summary>
    /// Insere o personagem. Retorna false se o slot já está em uso ou o nome é de outra conta (na mesma conta o
    /// nome pode repetir: o Arch tem o nome do Mortal).
    /// </summary>
    Task<bool> CreateAsync(Character character);

    Task<bool> DeleteAsync(int accountId, int slot);

    Task SavePositionAsync(int characterId, short x, short y);

    Task SaveInventoryAsync(int characterId, Item[] inventory);

    Task SaveExpAsync(int characterId, long exp);

    /// <summary>Grava experiência, nível, gold, vida/mana e pontos (depois de matar ou subir de nível).</summary>
    Task SaveProgressAsync(Character character);

    /// <summary>Grava equipamentos, inventário, gold e HP/MP do personagem.</summary>
    Task SaveItemsAsync(Character character);
}
