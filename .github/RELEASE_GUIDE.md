# Release Guide

This guide explains how to create releases for the SQL Server MCP Server project.

## Automated Release Process

The project uses GitHub Actions to automatically build and release binaries.

### How It Works

1. **On Pull Requests to `main`**:
   - Builds and tests on Windows, Linux, and macOS
   - Validates that the code compiles successfully
   - No release is created

2. **On Push to `main`**:
   - Builds and tests on all platforms
   - Validates the main branch
   - No release is created

3. **On Tag Push (e.g., `v1.0.0`)**:
   - Builds on all platforms
   - Creates self-contained executables for:
     - Windows x64
     - Linux x64
     - macOS x64 (Intel)
     - macOS ARM64 (Apple Silicon)
   - Packages binaries into archives
   - Creates a GitHub Release with all binaries attached
   - Auto-generates release notes

## Creating a New Release

### Step 1: Ensure Code is Ready
```bash
# Make sure you're on main and up to date
git checkout main
git pull origin main

# Verify everything builds locally
dotnet build SqlServerMcpServer/SqlServerMcpServer.csproj --configuration Release
```

### Step 2: Create and Push a Tag
```bash
# Create a version tag (use semantic versioning)
git tag -a v1.0.0 -m "Release version 1.0.0 - Initial release"

# Push the tag to GitHub
git push origin v1.0.0
```

### Step 3: Monitor the Workflow
1. Go to your repository on GitHub
2. Click on **Actions** tab
3. Watch the "Build and Release" workflow run
4. It will take ~5-10 minutes to complete

### Step 4: Verify the Release
1. Go to **Releases** in your repository
2. You should see a new release with:
   - Version tag (e.g., `v1.0.0`)
   - Release notes
   - Four binary packages attached

## Version Numbering

Use [Semantic Versioning](https://semver.org/):
- `v1.0.0` - Major release (breaking changes)
- `v1.1.0` - Minor release (new features, backwards compatible)
- `v1.0.1` - Patch release (bug fixes)

## Pre-releases

For beta or release candidate versions:
```bash
git tag -a v1.0.0-beta.1 -m "Beta release"
git push origin v1.0.0-beta.1
```

Mark as pre-release in the GitHub UI after creation.

## Troubleshooting

### Build Fails
- Check the Actions logs for specific errors
- Ensure .NET 10.0 SDK is properly configured
- Verify all dependencies are available

### Tag Already Exists
```bash
# Delete local tag
git tag -d v1.0.0

# Delete remote tag
git push origin :refs/tags/v1.0.0

# Create new tag
git tag -a v1.0.0 -m "Release version 1.0.0"
git push origin v1.0.0
```

### Release Not Created
- Ensure the tag matches the pattern `v*.*.*`
- Check that GitHub Actions has permission to create releases
- Verify `GITHUB_TOKEN` has appropriate permissions

## Manual Release (Alternative)

If you need to create a release manually:

```bash
# Build for all platforms
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o ./publish/win-x64
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true -o ./publish/linux-x64
dotnet publish -c Release -r osx-x64 --self-contained true /p:PublishSingleFile=true -o ./publish/osx-x64
dotnet publish -c Release -r osx-arm64 --self-contained true /p:PublishSingleFile=true -o ./publish/osx-arm64

# Create archives
cd publish/win-x64 && zip -r ../../SqlServerMcpServer-win-x64.zip * && cd ../..
cd publish/linux-x64 && tar -czf ../../SqlServerMcpServer-linux-x64.tar.gz * && cd ../..
cd publish/osx-x64 && tar -czf ../../SqlServerMcpServer-osx-x64.tar.gz * && cd ../..
cd publish/osx-arm64 && tar -czf ../../SqlServerMcpServer-osx-arm64.tar.gz * && cd ../..

# Create release using GitHub CLI
gh release create v1.0.0 \
  SqlServerMcpServer-win-x64.zip \
  SqlServerMcpServer-linux-x64.tar.gz \
  SqlServerMcpServer-osx-x64.tar.gz \
  SqlServerMcpServer-osx-arm64.tar.gz \
  --title "SQL Server MCP Server v1.0.0" \
  --notes "Release notes here"
```

## Best Practices

1. **Test Before Tagging**: Always test the main branch before creating a release tag
2. **Meaningful Commit Messages**: Write clear commit messages for the changelog
3. **Update Documentation**: Ensure README.md is up to date before releasing
4. **Changelog**: Consider maintaining a CHANGELOG.md file
5. **Security**: Never include sensitive data (connection strings, passwords) in releases
