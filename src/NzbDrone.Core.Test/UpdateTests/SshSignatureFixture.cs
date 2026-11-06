using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    // Files/Update holds a manifest signed by real ssh-keygen with a throwaway key, so these tests
    // check against what OpenSSH writes, not only against what this code would write.
    [TestFixture]
    public class SshSignatureFixture
    {
        private byte[] _manifest;
        private string _signature;
        private string _testKey;

        private static string Fixture(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Update", name);

        [SetUp]
        public void Setup()
        {
            _manifest = File.ReadAllBytes(Fixture("main.json"));
            _signature = File.ReadAllText(Fixture("main.json.sig"));
            _testKey = File.ReadAllText(Fixture("test-key.pub")).Trim();
        }

        [Test]
        public void should_accept_a_signature_made_by_ssh_keygen()
        {
            SshSignature.Verify(_manifest, _signature, _testKey, UpdateSigningKey.Namespace).Should().BeTrue();
        }

        [Test]
        public void should_refuse_a_manifest_changed_by_one_byte()
        {
            var tampered = _manifest.ToArray();
            tampered[tampered.Length / 2] ^= 1;

            SshSignature.Verify(tampered, _signature, _testKey, UpdateSigningKey.Namespace).Should().BeFalse();
        }

        [Test]
        public void should_refuse_a_manifest_with_its_line_endings_changed()
        {
            var crlf = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(_manifest).Replace("\n", "\r\n"));

            SshSignature.Verify(crlf, _signature, _testKey, UpdateSigningKey.Namespace).Should().BeFalse();
        }

        [Test]
        public void should_refuse_a_signature_for_another_namespace()
        {
            SshSignature.Verify(_manifest, _signature, _testKey, "git").Should().BeFalse();
        }

        [Test]
        public void should_refuse_a_signature_by_a_key_it_does_not_trust()
        {
            // The manifest is good, and signed: but not by Nodearr's release key.
            SshSignature.Verify(_manifest, _signature, UpdateSigningKey.PublicKey, UpdateSigningKey.Namespace).Should().BeFalse();
        }

        [TestCase("")]
        [TestCase("not a signature")]
        [TestCase("-----BEGIN SSH SIGNATURE-----\nAAAA\n-----END SSH SIGNATURE-----")]
        [TestCase("-----BEGIN SSH SIGNATURE-----\n!!!!\n-----END SSH SIGNATURE-----")]
        public void should_refuse_a_signature_it_cannot_read(string signature)
        {
            SshSignature.Verify(_manifest, signature, _testKey, UpdateSigningKey.Namespace).Should().BeFalse();
        }

        [Test]
        public void should_refuse_a_truncated_signature()
        {
            var lines = _signature.Trim().Split('\n');
            var truncated = string.Join("\n", lines.Take(3).Concat(lines.Skip(lines.Length - 1)));

            SshSignature.Verify(_manifest, truncated, _testKey, UpdateSigningKey.Namespace).Should().BeFalse();
        }

        [Test]
        public void should_accept_rsa_sha2_256_over_a_sha256_hash()
        {
            using var rsa = RSA.Create(2048);
            var publicKey = PublicKey(rsa);
            var message = Encoding.UTF8.GetBytes("{\"format\":1}\n");

            var signature = Sign(rsa, message, UpdateSigningKey.Namespace, "sha256", "rsa-sha2-256", HashAlgorithmName.SHA256);

            SshSignature.Verify(message, signature, publicKey, UpdateSigningKey.Namespace).Should().BeTrue();
        }

        [Test]
        public void should_refuse_the_old_ssh_rsa_signature_over_sha1()
        {
            using var rsa = RSA.Create(2048);
            var message = Encoding.UTF8.GetBytes("{\"format\":1}\n");

            var signature = Sign(rsa, message, UpdateSigningKey.Namespace, "sha512", "ssh-rsa", HashAlgorithmName.SHA1);

            SshSignature.Verify(message, signature, PublicKey(rsa), UpdateSigningKey.Namespace).Should().BeFalse();
        }

        [Test]
        public void should_refuse_a_key_under_2048_bits()
        {
            using var rsa = RSA.Create(1024);
            var message = Encoding.UTF8.GetBytes("{\"format\":1}\n");

            var signature = Sign(rsa, message, UpdateSigningKey.Namespace, "sha512", "rsa-sha2-512", HashAlgorithmName.SHA512);

            SshSignature.Verify(message, signature, PublicKey(rsa), UpdateSigningKey.Namespace).Should().BeFalse();
        }

        [Test]
        public void the_built_in_key_should_be_an_rsa_key_of_at_least_4096_bits()
        {
            var blob = SshSignature.ReadPublicKey(UpdateSigningKey.PublicKey);

            blob.Length.Should().BeGreaterThan(512);
        }

        [Test]
        public void build_update_signers_should_name_the_built_in_key_for_the_update_namespace()
        {
            var signers = File.ReadAllLines(Path.Combine(RepositoryRoot(), "build", "update-signers"))
                .Where(l => l.Trim().Length > 0 && !l.StartsWith('#'))
                .ToList();

            signers.Should().ContainSingle()
                .Which.Should().Be($"nodearr-release namespaces=\"{UpdateSigningKey.Namespace}\" {UpdateSigningKey.PublicKey}");
        }

        private static string RepositoryRoot()
        {
            var folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "build", "update-signers")))
            {
                folder = folder.Parent;
            }

            if (folder == null)
            {
                Assert.Ignore("Not run from inside the repository");
            }

            return folder.FullName;
        }

        private static string PublicKey(RSA rsa)
        {
            var parameters = rsa.ExportParameters(false);
            return "ssh-rsa " + Convert.ToBase64String(Blob(Text("ssh-rsa"), Mpint(parameters.Exponent), Mpint(parameters.Modulus)));
        }

        // What ssh-keygen -Y sign writes, made here so the hash and signature types can be chosen.
        private static string Sign(RSA rsa, byte[] message, string signatureNamespace, string hashName, string signatureType, HashAlgorithmName signatureHash)
        {
            var parameters = rsa.ExportParameters(false);
            var key = Blob(Text("ssh-rsa"), Mpint(parameters.Exponent), Mpint(parameters.Modulus));
            var hash = hashName == "sha256" ? SHA256.HashData(message) : SHA512.HashData(message);

            var signed = Concat(Encoding.ASCII.GetBytes("SSHSIG"), String(Text(signatureNamespace)), String(Array.Empty<byte>()), String(Text(hashName)), String(hash));
            var raw = rsa.SignData(signed, signatureHash, RSASignaturePadding.Pkcs1);

            var blob = Concat(
                Encoding.ASCII.GetBytes("SSHSIG"),
                new byte[] { 0, 0, 0, 1 },
                String(key),
                String(Text(signatureNamespace)),
                String(Array.Empty<byte>()),
                String(Text(hashName)),
                String(Blob(Text(signatureType), raw)));

            return "-----BEGIN SSH SIGNATURE-----\n" + Convert.ToBase64String(blob, Base64FormattingOptions.InsertLineBreaks) + "\n-----END SSH SIGNATURE-----\n";
        }

        private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

        private static byte[] Mpint(byte[] value) => (value[0] & 0x80) != 0 ? Concat(new byte[] { 0 }, value) : value;

        private static byte[] Blob(params byte[][] strings) => Concat(strings.Select(String).ToArray());

        private static byte[] String(byte[] bytes)
        {
            var length = bytes.Length;
            return Concat(new[] { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length }, bytes);
        }

        private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    }
}
