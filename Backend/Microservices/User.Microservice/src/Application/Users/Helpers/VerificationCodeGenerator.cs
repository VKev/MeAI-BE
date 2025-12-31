using System.Security.Cryptography;
using System.Text;

namespace Application.Users.Helpers;

public static class VerificationCodeGenerator
{
    private const int CodeLength = 6;

    public static string GenerateNumericCode()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt32(bytes) % 1_000_000;
        return value.ToString($"D{CodeLength}");
    }

    public static string HashCode(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToBase64String(bytes);
    }
}
