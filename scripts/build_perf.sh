#!/bin/bash

# Build with performance tracking enabled
echo "Building Shelltrac with performance tracking..."
dotnet build -p:DefineConstants=PERF_TRACKING

echo ""
echo "Running with performance tracking enabled:"
dotnet run test_cache.s

echo ""
echo "Performance tracking is now enabled! You'll see detailed [PERF] output."
echo ""
echo "To build without performance tracking (normal build):"
echo "  dotnet build"
echo "  dotnet run test_cache.s"