using UserManagementAPI.Models;

namespace UserManagementAPI.Services;

public enum UserOperationStatus { Success, NotFound, DuplicateEmail }

public record UserOperationResult(UserOperationStatus Status, User? User = null);

public interface IUserService
{
    (IReadOnlyList<User> Items, int TotalCount) GetPage(int page, int pageSize);
    User? GetById(int id);

    // Uniqueness checks happen inside these calls so check-and-write is atomic.
    UserOperationResult Create(UserRequest request);
    UserOperationResult Update(int id, UserRequest request);
    bool Delete(int id);
}
