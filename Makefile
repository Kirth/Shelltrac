fmt:
	docker run --rm -v $(pwd):/app -w /app csharpier .

fmt-build:
	docker build -t csharpier -f docker/csharpier.Dockerfile
