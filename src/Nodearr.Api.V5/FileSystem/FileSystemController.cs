using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Common.Disk;

namespace Nodearr.Api.V5.FileSystem;

[V5ApiController]
public class FileSystemController : Controller
{
    private readonly IFileSystemLookupService _fileSystemLookupService;

    public FileSystemController(IFileSystemLookupService fileSystemLookupService)
    {
        _fileSystemLookupService = fileSystemLookupService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<FileSystemResult> GetContents(string? path, bool includeFiles = false, bool allowFoldersWithoutTrailingSlashes = false)
    {
        return TypedResults.Ok(_fileSystemLookupService.LookupContents(path, includeFiles, allowFoldersWithoutTrailingSlashes));
    }
}
