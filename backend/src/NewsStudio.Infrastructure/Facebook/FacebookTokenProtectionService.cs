using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Facebook;

public class FacebookTokenProtectionService : IFacebookTokenProtectionService
{
    private readonly byte[] _key;

    public FacebookTokenProtectionService(IConfiguration configuration)
    {
        var rawKey = configuration["Facebook:TokenEncryptionKey"]
                     ?? configuration["TokenEncryptionKey"]
                     ?? "NewsStudio_FB_Secret_Key_2026_Secure_Key_32B!";

        // Derive standard 256-bit key using SHA-256
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
    }

    public string EncryptToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return string.Empty;
        }

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length); // Prepend IV

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(rawToken);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    public string DecryptToken(string encryptedToken)
    {
        if (string.IsNullOrWhiteSpace(encryptedToken))
        {
            return string.Empty;
        }

        try
        {
            var fullCipher = Convert.FromBase64String(encryptedToken);
            using var aes = Aes.Create();
            aes.Key = _key;

            var iv = new byte[aes.BlockSize / 8];
            Array.Copy(fullCipher, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);

            return sr.ReadToEnd();
        }
        catch
        {
            // Fallback for plain unencrypted tokens in test environments
            return encryptedToken;
        }
    }

    public string MaskToken(string rawOrEncryptedToken)
    {
        if (string.IsNullOrWhiteSpace(rawOrEncryptedToken))
        {
            return string.Empty;
        }

        if (rawOrEncryptedToken.Length <= 8)
        {
            return "****";
        }

        var prefix = rawOrEncryptedToken.Substring(0, 4);
        var suffix = rawOrEncryptedToken.Substring(rawOrEncryptedToken.Length - 4);
        return $"{prefix}••••••••{suffix}";
    }
}
