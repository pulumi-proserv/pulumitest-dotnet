# Default recipe - show help
default:
    @just --list

# Run all tests
test:
    dotnet test

# Run formatter and analyzers in check mode
lint:
    dotnet format --verify-no-changes

# Auto-fix formatting issues
lint-fix:
    dotnet format

# Build the solution
build:
    dotnet build

# Create the NuGet package in dist/
pack:
    dotnet pack src/PulumiTest -c Release -o dist

# Restore dependencies
install:
    dotnet restore

# Install git hooks (pre-push)
install-hooks:
    @echo "Installing git hooks..."
    cp scripts/hooks/pre-push .git/hooks/pre-push
    chmod +x .git/hooks/pre-push
    @echo "Git hooks installed"

# Clean build artifacts
clean:
    rm -rf dist/
    dotnet clean
