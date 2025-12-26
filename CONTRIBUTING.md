# Contributing to Local Recommendations

Thank you for your interest in contributing to the Local Recommendations plugin for Jellyfin! This document provides guidelines for contributing to the project.

## Code of Conduct

This project follows the Contributor Covenant Code of Conduct. By participating, you are expected to uphold this code.

## How Can I Contribute?

### Reporting Bugs

Before creating a bug report, please check existing issues to avoid duplicates. When creating a bug report, include:

- A clear, descriptive title
- Steps to reproduce the issue
- Expected vs. actual behavior
- Your environment (Jellyfin version, OS, .NET version)
- Plugin version
- Relevant log excerpts (sanitize any personal information)

Use the bug report issue template when available.

### Suggesting Features

Feature suggestions are welcome! Please:

- Check if the feature has already been requested
- Provide a clear use case
- Explain how it aligns with the plugin's privacy-first philosophy
- Consider implementation complexity

Use the feature request issue template when available.

### Pull Requests

1. **Fork and Clone**
   ```bash
   git clone https://github.com/YOUR_USERNAME/jellyfin-plugin-localrecs.git
   cd jellyfin-plugin-localrecs
   ```

2. **Create a Branch**
   ```bash
   git checkout -b feature/your-feature-name
   # or
   git checkout -b fix/your-bug-fix
   ```

3. **Set Up Development Environment**
   - Install .NET 9.0 SDK
   - Install Git Bash (Windows) or use native bash (Linux/Mac)

4. **Build and Test**
   ```bash
   # Build the plugin
   bash dotnet-helper.sh build
   
   # Run tests (excludes live integration tests)
   bash dotnet-helper.sh test --filter "Category!=LiveIntegration"
   
   # Run all tests (requires running Jellyfin server)
   bash dotnet-helper.sh test
   
   # Clean build artifacts
   bash dotnet-helper.sh clean
   ```

## Development Guidelines

### Code Style

This project follows Jellyfin's C# coding conventions:

- **Indentation**: 4 spaces (no tabs)
- **Braces**: Opening braces on new line
- **Naming**:
  - PascalCase for public members
  - camelCase with underscore prefix for private fields (`_fieldName`)
  - Use `var` for obvious types only
- **Types**: Strong typing with nullable reference types enabled
- **Error Handling**: Use try-catch with proper `ILogger<T>` logging

StyleCop analyzers run automatically during build. All warnings must be resolved.

### Architecture Principles

- **Separation of Concerns**: Services should have single, well-defined responsibilities
- **Dependency Injection**: Use constructor injection for all dependencies
- **Immutability**: Prefer immutable data structures (see `IReadOnlyList<T>`, `IReadOnlyDictionary<K,V>`)
- **Privacy**: No external API calls, no tracking, all processing local
- **Jellyfin Integration**: Use provided interfaces (`ILibraryManager`, `IUserManager`, `IUserDataManager`)

### Testing Requirements

- **Utility Functions**: Aim for 100% coverage
- **Domain Services**: Aim for >80% coverage
- **Integration Tests**: Cover critical user paths

**Test Categories**:
- **Unit**: Fast tests with no external dependencies (default)
- **Integration**: Full pipeline tests with mocked Jellyfin services
- **LiveIntegration**: Tests requiring a running Jellyfin server (slow, requires environment variables)

When running quick tests, use:
```bash
bash dotnet-helper.sh test --filter "Category!=LiveIntegration"
```

### Commit Messages

- Use present tense ("Add feature" not "Added feature")
- Use imperative mood ("Move cursor to..." not "Moves cursor to...")
- Reference issues and PRs when applicable
- Keep first line under 72 characters

### Before Submitting

- [ ] Code builds with zero warnings
- [ ] All tests pass
- [ ] New tests added for new functionality
- [ ] Documentation updated (README, DESIGN.md if applicable)
- [ ] Code follows style guidelines
- [ ] Commit messages are clear

## Project Structure

```
Jellyfin.Plugin.LocalRecs/
├── Configuration/        # Plugin config + admin UI
├── Models/              # Domain models (immutable)
├── Utilities/           # Pure utility functions
├── Services/            # Domain services
├── VirtualLibrary/      # .strm file management
├── ScheduledTasks/      # Background jobs
└── Api/                 # REST API controllers

Jellyfin.Plugin.LocalRecs.Tests/
├── Unit/                # Fast unit tests
├── Domain/              # Service tests
├── Integration/         # Pipeline tests
└── Fixtures/            # Test data generators
```

## Need Help?

- Browse [existing issues](https://github.com/rdpharr/jellyfin-plugin-localrecs/issues)
- Check [DESIGN.md](DESIGN.md) for architectural details
- Start a [Discussion](https://github.com/rdpharr/jellyfin-plugin-localrecs/discussions)

## License

By contributing, you agree that your contributions will be licensed under the GNU General Public License v3.0.
