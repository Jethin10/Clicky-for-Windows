namespace Clicky.Windows.Services;

public sealed class AgentWorkspaceService
{
    public IReadOnlyList<AgentFilePlan> Plan(string workspacePath, AgentArtifactPackage package)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new InvalidOperationException("Choose an agent workspace before approving files.");
        }

        var root = Path.GetFullPath(workspacePath.Trim());
        Directory.CreateDirectory(root);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<AgentFilePlan>();
        foreach (var file in package.Files)
        {
            var relativePath = file.Path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException($"Agent path must be relative: {file.Path}");
            }

            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Agent path escapes the workspace: {file.Path}");
            }
            EnsureNoReparsePoints(root, fullPath);
            if (!seen.Add(fullPath))
            {
                throw new InvalidOperationException($"Agent package contains a duplicate path: {file.Path}");
            }

            var normalizedRelative = Path.GetRelativePath(root, fullPath);
            plans.Add(new AgentFilePlan(
                normalizedRelative,
                fullPath,
                file.Content,
                File.Exists(fullPath)));
        }

        return plans;
    }

    public async Task<AgentWriteResult> ApplyAsync(
        string workspacePath,
        IReadOnlyList<AgentFilePlan> plans,
        CancellationToken cancellationToken)
    {
        var workspace = Path.GetFullPath(workspacePath.Trim());
        var backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clicky",
            "agent-backups",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        var created = new List<string>();
        var backups = new List<(string Target, string Backup)>();

        try
        {
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoReparsePoints(workspace, plan.FullPath);
                var directory = Path.GetDirectoryName(plan.FullPath)!;
                Directory.CreateDirectory(directory);

                if (plan.WillOverwrite)
                {
                    var backup = Path.Combine(backupRoot, plan.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(plan.FullPath, backup, overwrite: true);
                    backups.Add((plan.FullPath, backup));
                }
                else
                {
                    created.Add(plan.FullPath);
                }

                var temporary = plan.FullPath + $".clicky-{Guid.NewGuid():N}.tmp";
                await File.WriteAllTextAsync(temporary, plan.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                File.Move(temporary, plan.FullPath, overwrite: true);
            }

            return new AgentWriteResult(workspace, plans.Count, backups.Count, backupRoot);
        }
        catch
        {
            foreach (var path in created)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            foreach (var (target, backup) in backups)
            {
                if (File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(backup, target, overwrite: true);
                }
            }
            throw;
        }
    }

    private static void EnsureNoReparsePoints(string workspace, string target)
    {
        var root = Path.GetFullPath(workspace);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Agent workspaces cannot be symbolic links or junctions.");
        }

        var relative = Path.GetRelativePath(root, target);
        var current = root;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((Directory.Exists(current) || File.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Agent path crosses a symbolic link or junction: {relative}");
            }
        }
    }
}

public sealed record AgentFilePlan(
    string RelativePath,
    string FullPath,
    string Content,
    bool WillOverwrite)
{
    public string Action => WillOverwrite ? "Overwrite" : "Create";
    public int CharacterCount => Content.Length;
}

public sealed record AgentWriteResult(
    string WorkspacePath,
    int FilesWritten,
    int FilesBackedUp,
    string BackupPath);
