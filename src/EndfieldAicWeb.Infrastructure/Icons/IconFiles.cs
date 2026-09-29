using System.Security.Cryptography;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Infrastructure.Icons;

/// <summary>アイコン画像のバイト列とマニフェスト記述（Bytes/Sha256）の照合。</summary>
public static class IconFiles
{
    /// <summary>バイト列の Sha256 を小文字 hex で返す。</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>内容がマニフェスト記述（Bytes・Sha256）と一致するか。</summary>
    public static bool Matches(byte[] bytes, IconEntry entry) =>
        bytes.LongLength == entry.Bytes && Sha256Hex(bytes) == entry.Sha256;
}
