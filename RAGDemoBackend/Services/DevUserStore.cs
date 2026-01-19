using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace RAGDemoBackend.Services
{
    public interface IDevUserStore
    {
        bool TryRegister(string username, string password);
        bool ValidateCredentials(string username, string password, out string role);
    }

    public class DevUserStore : IDevUserStore
    {
        private readonly ConcurrentDictionary<string, string> _users = new();
        private readonly IConfiguration _configuration;

        public DevUserStore(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public bool TryRegister(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            var adminUser = _configuration["Auth:AdminUsername"] ?? "admin";
            if (string.Equals(username, adminUser, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var hash = HashPassword(password);
            return _users.TryAdd(username, hash);
        }

        public bool ValidateCredentials(string username, string password, out string role)
        {
            role = "user";
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            var adminUser = _configuration["Auth:AdminUsername"] ?? "admin";
            var adminPassword = _configuration["Auth:AdminPassword"] ?? string.Empty;

            if (string.Equals(username, adminUser, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(password, adminPassword, StringComparison.Ordinal))
            {
                role = "admin";
                return true;
            }

            if (_users.TryGetValue(username, out var storedHash))
            {
                return string.Equals(storedHash, HashPassword(password), StringComparison.Ordinal);
            }

            return false;
        }

        private static string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }
    }
}
