using System.Collections.Concurrent;

namespace WYD.Server.Network;

/// <summary>Distribui os IDs de conexão (1..MaxUsers) e controla quais contas estão online.</summary>
public sealed class SessionManager
{
    private readonly ConcurrentDictionary<ushort, ClientSession> _sessions = new();
    private readonly ConcurrentDictionary<string, ClientSession> _byAccount = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxUsers;
    private readonly object _idLock = new();
    private ushort _nextId = 1;

    public SessionManager(int maxUsers) => _maxUsers = maxUsers;

    public int Count => _sessions.Count;

    public IEnumerable<ClientSession> All => _sessions.Values;

    /// <summary>Reserva um ID livre. Retorna 0 se o servidor está cheio.</summary>
    public ushort ReserveId()
    {
        lock (_idLock)
        {
            if (_sessions.Count >= _maxUsers) return 0;
            for (int attempt = 0; attempt < _maxUsers; attempt++)
            {
                ushort id = _nextId;
                _nextId = (ushort)(_nextId % _maxUsers + 1);
                if (!_sessions.ContainsKey(id)) return id;
            }
            return 0;
        }
    }

    public void Add(ClientSession session)
    {
        _sessions[session.Id] = session;
        session.Closed += Remove;
    }

    /// <summary>Marca a conta como online. Retorna a sessão anterior, se a conta já estava conectada.</summary>
    public ClientSession? BindAccount(string username, ClientSession session)
    {
        ClientSession? previous = null;
        _byAccount.AddOrUpdate(username, session, (_, old) =>
        {
            previous = old;
            return session;
        });
        return previous == session ? null : previous;
    }

    public bool IsOnline(string username) => _byAccount.ContainsKey(username);

    private void Remove(ClientSession session)
    {
        _sessions.TryRemove(session.Id, out _);
        if (session.Account is { } account)
            _byAccount.TryRemove(new KeyValuePair<string, ClientSession>(account.Username, session));
    }
}
