using System;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Update
{
    public interface IVerifyUpdates
    {
        bool Verify(UpdatePackage updatePackage, string packagePath);
    }

    // A downloaded package is installed only if it is the size and has the SHA-256 the signed
    // manifest gave for it.
    public class UpdateVerification : IVerifyUpdates
    {
        private readonly IDiskProvider _diskProvider;

        public UpdateVerification(IDiskProvider diskProvider)
        {
            _diskProvider = diskProvider;
        }

        public bool Verify(UpdatePackage updatePackage, string packagePath)
        {
            if (string.IsNullOrWhiteSpace(updatePackage.Hash))
            {
                return false;
            }

            if (updatePackage.Size > 0 && _diskProvider.GetFileSize(packagePath) != updatePackage.Size)
            {
                return false;
            }

            using (var fileStream = _diskProvider.OpenReadStream(packagePath))
            {
                var hash = fileStream.SHA256Hash();

                return hash.Equals(updatePackage.Hash, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
