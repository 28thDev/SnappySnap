using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SnappySnap.Core;

namespace SnappySnap.Updates;

public sealed class ReleaseVerifier(string publicKey)
{
    public static ReleaseVerifier Production()
    {
        using var stream = typeof(ReleaseVerifier).Assembly.GetManifestResourceStream("SnappySnap.Updates.release-public.pem")!;
        using var reader = new StreamReader(stream);
        return new(reader.ReadToEnd());
    }
    public static Version ParseVersion(string text)
    {
        if (text is null || !Regex.IsMatch(text, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)
            || !Version.TryParse(text, out var version)) throw new InvalidDataException("Invalid release version.");
        return version;
    }
    public UpdateRelease Read(byte[] manifest, byte[] signature, int windowsBuild)
    {
        if (manifest.Length > 65536 || signature.Length != 64) throw new InvalidDataException("Invalid release catalog size.");
        using var key = ECDsa.Create(); key.ImportFromPem(publicKey);
        if (key.KeySize != 256 || !key.VerifyData(manifest, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new CryptographicException("Release signature is invalid. The update was not trusted.");
        var release = JsonSerializer.Deserialize<UpdateRelease>(manifest, JsonOptions) ?? throw new InvalidDataException("Empty release catalog.");
        ParseVersion(release.Version);
        if (release.FileName != $"SnappySnap-Setup-{release.Version}-x64.exe") throw new InvalidDataException("Invalid installer filename.");
        if (release.Platform != "win-x64" || release.MinimumWindowsBuild < 22000 || release.MinimumWindowsBuild > windowsBuild)
            throw new InvalidDataException("This release does not support this Windows version or platform.");
        if (release.Size <= 0 || release.Size > 2L * 1024 * 1024 * 1024 || release.Sha256 is null || !Regex.IsMatch(release.Sha256, "^[A-Fa-f0-9]{64}$")
            || release.Notes is null || release.Notes.Length > 12000 || release.PublishedUtc == default)
            throw new InvalidDataException("Invalid release metadata.");
        return release;
    }
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static async Task VerifyPackageAsync(Stream stream, UpdateRelease release, CancellationToken ct)
    {
        if (stream.Length != release.Size) throw new InvalidDataException("Installer size does not match the signed catalog.");
        stream.Position = 0;
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(release.Sha256)))
            throw new CryptographicException("Installer checksum does not match the signed catalog.");
        stream.Position = 0;
    }
    public static async Task<byte[]> ReadBoundedAsync(string path, int limit, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (file.Length > limit) throw new InvalidDataException("Release file exceeds its size limit.");
        var data = new byte[(int)file.Length]; await file.ReadExactlyAsync(data, ct).ConfigureAwait(false); return data;
    }
    public static async Task<byte[]> ReadBoundedAsync(Stream source, int limit, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)output.Length)), ct).ConfigureAwait(false)) != 0)
        {
            output.Write(buffer, 0, count);
            if (output.Length > limit) throw new InvalidDataException("Release file exceeds its size limit.");
        }
        return output.ToArray();
    }
}
