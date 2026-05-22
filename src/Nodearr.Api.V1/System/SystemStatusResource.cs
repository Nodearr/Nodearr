namespace Nodearr.Api.V1.System;

public sealed record SystemStatusResource(
    string AppName,
    string InstanceName,
    string Version,
    DateTime StartTime,
    string RuntimeVersion,
    string RuntimeName,
    string OsDescription,
    bool IsProduction);
