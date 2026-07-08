namespace Indexarr.Web.Services;

public sealed class StoragePathResolver
{
    private readonly IWebHostEnvironment _environment;

    public StoragePathResolver(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public string ResolveFilePath(string configuredPath, string fileName)
    {
        var normalizedDirectory = ResolveDirectory(configuredPath);
        Directory.CreateDirectory(normalizedDirectory);
        return Path.Combine(normalizedDirectory, fileName);
    }

    public string ResolveDirectory(string configuredPath)
    {
        if (OperatingSystem.IsWindows() && configuredPath.StartsWith('/'))
        {
            return Path.Combine(_environment.ContentRootPath, configuredPath.Trim('/').Replace('/', Path.DirectorySeparatorChar));
        }

        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        return Path.Combine(_environment.ContentRootPath, configuredPath);
    }
}
