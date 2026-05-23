using System;
using Microsoft.AspNetCore.Mvc;

namespace Nodearr.Http.REST.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RestPostByIdAttribute : HttpPostAttribute
    {
    }
}
