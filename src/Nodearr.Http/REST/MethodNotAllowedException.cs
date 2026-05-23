using System.Net;
using Nodearr.Http.Exceptions;

namespace Nodearr.Http.REST
{
    public class MethodNotAllowedException : ApiException
    {
        public MethodNotAllowedException(object content = null)
            : base(HttpStatusCode.MethodNotAllowed, content)
        {
        }
    }
}
