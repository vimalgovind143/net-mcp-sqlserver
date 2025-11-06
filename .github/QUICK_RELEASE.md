# Quick Release Reference

## TL;DR - Create a Release

```bash
# 1. Make sure main is ready
git checkout main
git pull origin main

# 2. Create and push a tag
git tag -a v1.0.0 -m "Release v1.0.0"
git push origin v1.0.0

# 3. Wait ~5-10 minutes, check GitHub Actions
# 4. Release will be created automatically with binaries
```

## What Happens Automatically

✅ **On Pull Request to main**:
- Builds on Windows, Linux, macOS
- Validates code compiles
- No release created

✅ **On Tag Push (v*.*.*)**: 
- Builds self-contained executables for all platforms
- Creates GitHub Release
- Attaches binaries (Windows, Linux, macOS Intel, macOS ARM)
- Generates release notes

## Version Format

- `v1.0.0` - Major release
- `v1.1.0` - Minor release  
- `v1.0.1` - Patch release
- `v1.0.0-beta.1` - Pre-release

## Delete a Tag (if needed)

```bash
git tag -d v1.0.0              # Delete locally
git push origin :refs/tags/v1.0.0  # Delete remotely
```

## Check Workflow Status

Go to: `https://github.com/YOUR_USERNAME/cshap-mcp-sqlserver/actions`
