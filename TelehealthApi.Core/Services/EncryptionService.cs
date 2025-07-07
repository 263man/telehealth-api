using Microsoft.Extensions.Configuration; // Required to access configuration
using System.Security.Cryptography;
using System.Text;

namespace TelehealthApi.Core.Services
{
    public class EncryptionService
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;

        // Constructor to inject configuration and retrieve encryption key/IV
        public EncryptionService(IConfiguration configuration)
        {
            // Retrieve Key and IV from configuration.
            // Ensure they are 32 bytes (256 bits) for AES-256 Key
            // and 16 bytes (128 bits) for AES IV. Pad them if needed.
            var keyString = configuration["Encryption:Key"] ?? throw new ArgumentNullException(nameof(configuration), "Encryption:Key configuration is missing.");
            var ivString = configuration["Encryption:IV"] ?? throw new ArgumentNullException(nameof(configuration), "Encryption:IV configuration is missing.");

            _key = Encoding.UTF8.GetBytes(keyString.PadRight(32, '\0')).Take(32).ToArray(); // Ensure 32 bytes
            _iv = Encoding.UTF8.GetBytes(ivString.PadRight(16, '\0')).Take(16).ToArray();   // Ensure 16 bytes
        }

        public string Encrypt(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
            using var sw = new StreamWriter(cs);

            sw.Write(input);
            sw.Flush(); // Flush the StreamWriter
            cs.FlushFinalBlock(); // Flush the CryptoStream to ensure all data is written

            return Convert.ToBase64String(ms.ToArray());
        }

        public string Decrypt(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            try
            {
                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;

                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream(Convert.FromBase64String(input));
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);

                return sr.ReadToEnd();
            }
            catch (CryptographicException ex)
            {
                // Log the error if decryption fails (e.g., invalid key/IV, corrupted data)
                // For a real app, you might want to return null, throw a custom exception, or provide a default value.
                Console.WriteLine($"Decryption error: {ex.Message}");
                return $"[Decryption Failed: {input}]"; // Or throw new InvalidDataException("Could not decrypt data", ex);
            }
        }
    }
}