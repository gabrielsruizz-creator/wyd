using WYD.Server.Game;

namespace WYD.Server.Database;

public interface IAccountRepository
{
    Task<Account?> FindByUsernameAsync(string username);
    Task<Account> CreateAsync(string username, string passwordHash);
    Task UpdateLastLoginAsync(int accountId, string? ip);
}

public interface ICharacterRepository
{
    Task<List<Character>> ListByAccountAsync(int accountId);
    Task<bool> NameExistsAsync(string name);

    /// <summary>Insere o personagem. Retorna false se o nome ou o slot já estão em uso.</summary>
    Task<bool> CreateAsync(Character character);

    Task<bool> DeleteAsync(int accountId, int slot);
}
