using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.Security;

[TestClass]
public sealed class DataProtectionKeyEncryptionTests
{
    [TestMethod]
    public async Task WithCertificate_KeysAreStoredEncrypted()
    {
        var pfxPath = Path.Combine(Path.GetTempPath(), $"vernetzt-dp-{Guid.NewGuid():N}.pfx");
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=Vernetzt DataProtection Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            await File.WriteAllBytesAsync(pfxPath, cert.Export(X509ContentType.Pfx, "test-password"));
        }

        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseSetting("DataProtection:CertificatePath", pfxPath);
                builder.UseSetting("DataProtection:CertificatePassword", "test-password");
            });

            var key = factory.Services.GetRequiredService<IKeyManager>()
                .CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VernetztDbContext>();
            var stored = await db.DataProtectionKeys.SingleAsync(k => k.FriendlyName == $"key-{key.KeyId}");

            StringAssert.Contains(stored.Xml, "encryptedSecret");
            Assert.IsFalse(stored.Xml!.Contains("<masterKey", StringComparison.Ordinal), "The master key must not be stored in plain text.");
        }
        finally
        {
            File.Delete(pfxPath);
        }
    }
}
