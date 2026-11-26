using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

public record DirectoryResource(string Name, string Path);

public record FileSystemResource(string Path, string? Parent, List<DirectoryResource> Directories);

/// <summary>
/// Lets the UI browse the server's folders when picking a root folder.
/// Only directory names are returned, never file contents.
/// </summary>
[ApiController]
[Route("api/v1/filesystem")]
public class FileSystemController : ControllerBase
{
    [HttpGet]
    public ActionResult<FileSystemResource> Get([FromQuery] string? path, [FromQuery] bool includeHidden = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Roots();
        }

        if (!System.IO.Path.IsPathRooted(path))
        {
            return ValidationProblem("Path must be absolute");
        }

        string full;
        try
        {
            full = System.IO.Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return ValidationProblem("Invalid path");
        }

        var dir = new DirectoryInfo(full);
        if (!dir.Exists)
        {
            return NotFound(new { message = $"Folder not found: {full}" });
        }

        List<DirectoryResource> children;
        try
        {
            children = dir.EnumerateDirectories()
                .Where(d => includeHidden || !IsHidden(d))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new DirectoryResource(d.Name, d.FullName))
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = $"Permission denied: {full}" });
        }
        catch (IOException e)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = e.Message });
        }

        return new FileSystemResource(dir.FullName, dir.Parent?.FullName, children);
    }

    private static FileSystemResource Roots()
    {
        if (OperatingSystem.IsWindows())
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new DirectoryResource(d.Name, d.RootDirectory.FullName))
                .ToList();
            return new FileSystemResource("", null, drives);
        }

        var root = new DirectoryInfo("/");
        var dirs = root.EnumerateDirectories()
            .Where(d => !IsHidden(d) && !SystemDirs.Contains(d.Name))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new DirectoryResource(d.Name, d.FullName))
            .ToList();
        return new FileSystemResource("/", null, dirs);
    }

    // Folders at "/" that never hold a media library; hiding them keeps the picker tidy.
    private static readonly HashSet<string> SystemDirs =
    [
        "bin", "boot", "dev", "etc", "lib", "lib32", "lib64", "libx32", "proc", "run", "sbin", "sys", "usr", "var", "snap", "lost+found",
    ];

    private static bool IsHidden(DirectoryInfo d) =>
        d.Name.StartsWith('.') || (OperatingSystem.IsWindows() && d.Attributes.HasFlag(FileAttributes.Hidden));
}
