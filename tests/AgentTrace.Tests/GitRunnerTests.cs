using AgentTrace.Services;

namespace AgentTrace.Tests;

public class GitRunnerTests
{
    [Fact]
    public void RunGit_ReturnsNull_ForBadDirectory()
    {
        var result = GitRunner.RunGit("/nonexistent/path/12345", "status");
        Assert.Null(result);
    }

    [Fact]
    public void RunGit_ReturnsOutput_ForValidRepo()
    {
        // Walk up from the test assembly to find the repo root (.git directory)
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);

        if (dir == null)
        {
            Assert.Skip("Could not locate .git directory");
            return;
        }

        var result = GitRunner.RunGit(dir, "rev-parse --is-inside-work-tree");
        Assert.NotNull(result);
        Assert.Contains("true", result);
    }
}
