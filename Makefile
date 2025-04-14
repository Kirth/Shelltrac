fmt:
	docker run --rm -v $(pwd):/app -w /app mcr.microsoft.com/dotnet/sdk:8.0 dotnet tool install --tool-path /tools csharpier && /tools/csharpier .
