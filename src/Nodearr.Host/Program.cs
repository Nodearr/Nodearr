using Microsoft.Extensions.FileProviders;
using Nodearr.Api.V1.System;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddApplicationPart(typeof(SystemController).Assembly);

var app = builder.Build();

var uiPath = Path.GetFullPath(Path.Combine(
    builder.Environment.ContentRootPath,
    "..",
    "..",
    "_output",
    "UI"));

Directory.CreateDirectory(uiPath);

var uiFileProvider = new PhysicalFileProvider(uiPath);

app.MapGet("/ping", () => Results.Ok(new { ok = true, app = "Nodearr" }));

app.MapGet("/initialize.json", (IWebHostEnvironment environment) => Results.Ok(new
{
    apiKey = string.Empty,
    apiRoot = "/api/v1",
    instanceName = "Nodearr",
    theme = "dark",
    urlBase = string.Empty,
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
    isProduction = !environment.IsDevelopment()
}));

app.MapControllers();

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = uiFileProvider
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = uiFileProvider
});

app.MapGet("/health", () => Results.Ok("OK"));

app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexPath = Path.Combine(uiPath, "index.html");

    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync($"Nodearr UI has not been built yet. Run `yarn build` or `npm run build` to create {indexPath}.");
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

app.Run();