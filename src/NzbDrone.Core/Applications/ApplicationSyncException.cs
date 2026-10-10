using System;
using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Applications
{
    public class ApplicationSyncException : NzbDroneException
    {
        public ApplicationSyncException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
