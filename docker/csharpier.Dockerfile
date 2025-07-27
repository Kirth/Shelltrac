# Save this as Dockerfile.csharpier
FROM mcr.microsoft.com/dotnet/sdk:9.0

# Install CSharpier globally
RUN dotnet tool install -g csharpier && \
  echo 'export PATH="$PATH:/root/.dotnet/tools"' >> ~/.bashrc

# Set up entry point
ENTRYPOINT ["/root/.dotnet/tools/dotnet-csharpier"]
