using UserManagementAPI.Models;

namespace UserManagementAPI.Services;

// Registered as a singleton, so every operation runs under one lock to stay consistent across concurrent requests.
public class InMemoryUserService : IUserService
{
    private readonly object _lock = new();
    private readonly SortedDictionary<int, User> _users = new();                                   // kept in ID order, no per-request sort
    private readonly Dictionary<string, int> _idsByEmail = new(StringComparer.OrdinalIgnoreCase);   // O(1) duplicate-email lookup
    private int _nextId;

    public InMemoryUserService()
    {
        Create(new UserRequest { FirstName = "Alice", LastName = "Johnson", Email = "alice.johnson@techhive.com", Department = "HR" });
        Create(new UserRequest { FirstName = "Bob", LastName = "Smith", Email = "bob.smith@techhive.com", Department = "IT" });
    }

    public (IReadOnlyList<User> Items, int TotalCount) GetPage(int page, int pageSize)
    {
        lock (_lock)
        {
            // long math: (page - 1) * pageSize overflows int for large page numbers.
            var skip = (long)(page - 1) * pageSize;
            if (skip >= _users.Count)
                return ([], _users.Count);

            return (_users.Values.Skip((int)skip).Take(pageSize).ToList(), _users.Count);
        }
    }

    public User? GetById(int id)
    {
        lock (_lock)
            return _users.GetValueOrDefault(id);
    }

    public UserOperationResult Create(UserRequest request)
    {
        var email = request.Email.Trim();
        lock (_lock)
        {
            if (_idsByEmail.ContainsKey(email))
                return new(UserOperationStatus.DuplicateEmail);

            var user = new User
            {
                Id = ++_nextId,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Email = email,
                Department = request.Department.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            _users[user.Id] = user;
            _idsByEmail[email] = user.Id;
            return new(UserOperationStatus.Success, user);
        }
    }

    public UserOperationResult Update(int id, UserRequest request)
    {
        var email = request.Email.Trim();
        lock (_lock)
        {
            if (!_users.TryGetValue(id, out var existing))
                return new(UserOperationStatus.NotFound);

            if (_idsByEmail.TryGetValue(email, out var ownerId) && ownerId != id)
                return new(UserOperationStatus.DuplicateEmail);

            var updated = existing with
            {
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Email = email,
                Department = request.Department.Trim(),
                UpdatedAt = DateTime.UtcNow
            };
            _idsByEmail.Remove(existing.Email);
            _idsByEmail[email] = id;
            _users[id] = updated;
            return new(UserOperationStatus.Success, updated);
        }
    }

    public bool Delete(int id)
    {
        lock (_lock)
        {
            if (!_users.Remove(id, out var removed))
                return false;

            _idsByEmail.Remove(removed.Email);
            return true;
        }
    }
}
