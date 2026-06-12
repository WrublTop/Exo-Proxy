using System.Security.Cryptography;
using System.Text;

namespace ExoProxy.Data;

// Single source of truth for password hashing. Registration, login and
// password change MUST agree on the algorithm — keep it in one place.
public static class PasswordHasher
{
    public static string Hash(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
}
