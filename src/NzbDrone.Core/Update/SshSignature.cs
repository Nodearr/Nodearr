using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NzbDrone.Core.Update
{
    // Checks a signature made with `ssh-keygen -Y sign` (OpenSSH's PROTOCOL.sshsig), as git checks a
    // signed tag. Only RSA keys are accepted, signed with rsa-sha2-512 or rsa-sha2-256, because .NET
    // checks those without another package (ADR 0024).
    //
    // What is signed is not the message itself but this, built from the signature's own fields:
    //
    //   "SSHSIG" || string(namespace) || string(reserved) || string(hash algorithm) || string(H(message))
    //
    // so a signature made for one namespace, such as git's, cannot be used for another.
    public static class SshSignature
    {
        private const string Magic = "SSHSIG";
        private const string BeginMarker = "-----BEGIN SSH SIGNATURE-----";
        private const string EndMarker = "-----END SSH SIGNATURE-----";

        // Whether `armoredSignature` is a good signature of `message` in `signatureNamespace` by
        // `publicKey`, given as one line of an authorized_keys file: "ssh-rsa AAAA... comment".
        // Anything that cannot be read is a bad signature, never an exception.
        public static bool Verify(byte[] message, string armoredSignature, string publicKey, string signatureNamespace)
        {
            try
            {
                return VerifyOrThrow(message, armoredSignature, publicKey, signatureNamespace);
            }
            catch (Exception e) when (e is FormatException or EndOfStreamException or CryptographicException or ArgumentException or OverflowException)
            {
                return false;
            }
        }

        // The key's blob, from one line of an authorized_keys file. Throws FormatException.
        public static byte[] ReadPublicKey(string publicKey)
        {
            var parts = (publicKey ?? string.Empty).Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2 || parts[0] != "ssh-rsa")
            {
                throw new FormatException("Expected an ssh-rsa public key");
            }

            var blob = Convert.FromBase64String(parts[1]);

            using var reader = new SshReader(blob);

            if (reader.ReadText() != "ssh-rsa")
            {
                throw new FormatException("The key's type does not match its blob");
            }

            return blob;
        }

        private static bool VerifyOrThrow(byte[] message, string armoredSignature, string publicKey, string signatureNamespace)
        {
            var trustedKey = ReadPublicKey(publicKey);
            var blob = Unarmor(armoredSignature);

            using var reader = new SshReader(blob);

            if (Encoding.ASCII.GetString(reader.ReadRaw(Magic.Length)) != Magic)
            {
                return false;
            }

            if (reader.ReadUInt32() != 1)
            {
                return false;
            }

            var signingKey = reader.ReadString();
            var signedNamespace = reader.ReadText();
            var reserved = reader.ReadString();
            var hashAlgorithm = reader.ReadText();
            var signature = reader.ReadString();

            if (!reader.AtEnd || !signingKey.AsSpan().SequenceEqual(trustedKey) || signedNamespace != signatureNamespace)
            {
                return false;
            }

            var messageHash = hashAlgorithm switch
            {
                "sha512" => SHA512.HashData(message),
                "sha256" => SHA256.HashData(message),
                _ => null
            };

            if (messageHash == null)
            {
                return false;
            }

            using var signatureReader = new SshReader(signature);
            var signatureType = signatureReader.ReadText();
            var signatureBytes = signatureReader.ReadString();

            var signatureHash = signatureType switch
            {
                "rsa-sha2-512" => HashAlgorithmName.SHA512,
                "rsa-sha2-256" => HashAlgorithmName.SHA256,
                _ => default
            };

            if (signatureHash == default || !signatureReader.AtEnd)
            {
                return false;
            }

            var signedData = new SshWriter()
                .Raw(Encoding.ASCII.GetBytes(Magic))
                .String(Encoding.UTF8.GetBytes(signedNamespace))
                .String(reserved)
                .String(Encoding.UTF8.GetBytes(hashAlgorithm))
                .String(messageHash)
                .ToArray();

            using var rsa = RSA.Create();
            rsa.ImportParameters(ReadRsaParameters(trustedKey));

            // A key under 2048 bits is not one anybody should still trust.
            if (rsa.KeySize < 2048)
            {
                return false;
            }

            return rsa.VerifyData(signedData, signatureBytes, signatureHash, RSASignaturePadding.Pkcs1);
        }

        private static RSAParameters ReadRsaParameters(byte[] keyBlob)
        {
            using var reader = new SshReader(keyBlob);
            reader.ReadText();

            return new RSAParameters
            {
                Exponent = TrimSign(reader.ReadString()),
                Modulus = TrimSign(reader.ReadString())
            };
        }

        // An SSH mpint is two's complement, so a positive number whose top bit is set has a zero byte
        // in front. .NET wants the number without it.
        private static byte[] TrimSign(byte[] mpint)
        {
            var start = 0;

            while (start < mpint.Length - 1 && mpint[start] == 0)
            {
                start++;
            }

            return mpint[start..];
        }

        private static byte[] Unarmor(string armored)
        {
            var text = (armored ?? string.Empty).Replace("\r", string.Empty).Trim();
            var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
            var end = text.IndexOf(EndMarker, StringComparison.Ordinal);

            if (begin != 0 || end < 0 || end + EndMarker.Length != text.Length)
            {
                throw new FormatException("Not an SSH signature");
            }

            var body = text.Substring(BeginMarker.Length, end - BeginMarker.Length);

            return Convert.FromBase64String(string.Concat(body.Where(c => !char.IsWhiteSpace(c))));
        }

        private sealed class SshReader : IDisposable
        {
            private readonly BinaryReader _reader;
            private readonly MemoryStream _stream;

            public SshReader(byte[] data)
            {
                _stream = new MemoryStream(data, false);
                _reader = new BinaryReader(_stream);
            }

            public bool AtEnd => _stream.Position == _stream.Length;

            public byte[] ReadRaw(int count)
            {
                var bytes = _reader.ReadBytes(count);

                if (bytes.Length != count)
                {
                    throw new EndOfStreamException();
                }

                return bytes;
            }

            public uint ReadUInt32()
            {
                var bytes = ReadRaw(4);
                return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            }

            public byte[] ReadString()
            {
                var length = ReadUInt32();

                if (length > _stream.Length - _stream.Position)
                {
                    throw new EndOfStreamException();
                }

                return ReadRaw((int)length);
            }

            public string ReadText() => Encoding.UTF8.GetString(ReadString());

            public void Dispose()
            {
                _reader.Dispose();
            }
        }

        private sealed class SshWriter
        {
            private readonly MemoryStream _stream = new MemoryStream();

            public SshWriter Raw(byte[] bytes)
            {
                _stream.Write(bytes);
                return this;
            }

            public SshWriter String(byte[] bytes)
            {
                var length = (uint)bytes.Length;
                _stream.Write(new[] { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length });
                _stream.Write(bytes);
                return this;
            }

            public byte[] ToArray() => _stream.ToArray();
        }
    }
}
