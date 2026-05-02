using FluentAssertions;
using Xunit;

namespace SqlServerMcpServer.Tests;

public class DockerDeploymentTests
{
    [Fact]
    public void Dockerfile_HealthCheck_UsesHttpEndpointForSseTransport()
    {
        var dockerfile = ReadRepoFile("Dockerfile");

        dockerfile.Should().Contain("ENV MCP_TRANSPORT=sse");
        dockerfile.Should().Contain("EXPOSE 8080");
        dockerfile.Should().Contain("HEALTHCHECK");
        dockerfile.Should().Contain("curl -fsS \"http://localhost:${MCP_PORT:-8080}/health\"");
        dockerfile.Should().Contain("pgrep SqlServerMcpServer");
    }

    [Fact]
    public void Dockerfile_RuntimeImage_InstallsHealthCheckUtilities()
    {
        var dockerfile = ReadRepoFile("Dockerfile");

        dockerfile.Should().Contain("apt-get install -y --no-install-recommends curl procps");
        dockerfile.Should().Contain("rm -rf /var/lib/apt/lists/*");
    }

    [Fact]
    public void DockerCompose_HealthCheck_MatchesContainerRuntimeTools()
    {
        var compose = ReadRepoFile("docker-compose.yml");

        compose.Should().Contain("curl -fsS http://localhost:$${MCP_PORT:-8080}/health");
        compose.Should().Contain("pgrep SqlServerMcpServer");
        compose.Should().NotContain("wget ");
    }

    [Fact]
    public void DockerCompose_DoesNotBindMissingRootAppsettingsByDefault()
    {
        var compose = ReadRepoFile("docker-compose.yml");

        compose.Should().NotContain("version: \"3.9\"");
        compose.Should().NotContain("./appsettings.json:/app/appsettings.json:ro");
        compose.Should().Contain("#   - ./config-samples/appsettings.json:/app/appsettings.json:ro");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
