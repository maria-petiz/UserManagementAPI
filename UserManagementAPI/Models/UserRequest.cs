using System.ComponentModel.DataAnnotations;

namespace UserManagementAPI.Models;

// Shared payload for POST and PUT. [ApiController] returns 400 automatically when validation fails.
// Patterns tolerate surrounding whitespace because the service trims values before storing them.
public class UserRequest
{
    // Letters (any language), with inner spaces, apostrophes, periods and hyphens: "Mary-Jane", "O'Brien".
    private const string NamePattern = @"^\s*\p{L}[\p{L}\p{M}' .\-]*\s*$";

    [Required, StringLength(50)]
    [RegularExpression(NamePattern, ErrorMessage = "First name must start with a letter and contain only letters, spaces, apostrophes, periods or hyphens.")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [RegularExpression(NamePattern, ErrorMessage = "Last name must start with a letter and contain only letters, spaces, apostrophes, periods or hyphens.")]
    public string LastName { get; set; } = string.Empty;

    // [EmailAddress] alone accepts "a@b" and "x y@z.com"; require no whitespace and a dotted domain.
    [Required, StringLength(100)]
    [RegularExpression(@"^\s*[^@\s]+@[^@\s]+\.[^@\s]+\s*$", ErrorMessage = "Email must be a valid address, e.g. name@company.com.")]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [RegularExpression(@"^\s*[\p{L}\p{N}][\p{L}\p{N} &/\-]*\s*$", ErrorMessage = "Department must start with a letter or digit and contain only letters, digits, spaces, '&', '/' or '-'.")]
    public string Department { get; set; } = string.Empty;
}
