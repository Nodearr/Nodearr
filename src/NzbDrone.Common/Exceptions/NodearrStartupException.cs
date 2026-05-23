using System;

namespace NzbDrone.Common.Exceptions
{
    public class NodearrStartupException : NzbDroneException
    {
        public NodearrStartupException(string message, params object[] args)
            : base("Nodearr failed to start: " + string.Format(message, args))
        {
        }

        public NodearrStartupException(string message)
            : base("Nodearr failed to start: " + message)
        {
        }

        public NodearrStartupException()
            : base("Nodearr failed to start")
        {
        }

        public NodearrStartupException(Exception innerException, string message, params object[] args)
            : base("Nodearr failed to start: " + string.Format(message, args), innerException)
        {
        }

        public NodearrStartupException(Exception innerException, string message)
            : base("Nodearr failed to start: " + message, innerException)
        {
        }

        public NodearrStartupException(Exception innerException)
            : base("Nodearr failed to start: " + innerException.Message)
        {
        }
    }
}
