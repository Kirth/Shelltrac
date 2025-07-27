# Test script for @cache attribute functionality

# Test basic caching with default TTL
@cache
fn expensive_calculation(n) {
    log "Computing expensive calculation for #{n}"
    let result = n * n + 42
    return result
}

# Test caching with custom TTL using time literals
@cache(ttl: 30s)
fn quick_cache_function(x) {
    log "Quick cache function called with #{x}"
    return x * 2
}

# Test caching with time literal in minutes
@cache(ttl: 2m)
fn medium_cache_function(a, b) {
    log "Medium cache function called with #{a}, #{b}"
    return a + b
}

# Test time literal assignments
let wait_time = 5m
log "Wait time is #{wait_time.TotalMilliseconds}ms"

let short_time = 30s
log "Short time is #{short_time.TotalMilliseconds}ms"

# Test cache functionality
log "=== Testing Cache Functionality ==="

# First call - should execute and cache
log "First call to expensive_calculation(5):"
let result1 = expensive_calculation(5)
log "Result: #{result1}"

# Second call - should use cache (no log message from function)
log "Second call to expensive_calculation(5):"
let result2 = expensive_calculation(5)
log "Result: #{result2}"

# Different argument - should execute again
log "Call to expensive_calculation(10):"
let result3 = expensive_calculation(10)
log "Result: #{result3}"

# Test quick cache function
log "=== Testing Quick Cache Function ==="
log "First call to quick_cache_function(7):"
let quick1 = quick_cache_function(7)
log "Result: #{quick1}"

log "Second call to quick_cache_function(7):"
let quick2 = quick_cache_function(7)
log "Result: #{quick2}"

# Test medium cache function with multiple parameters
log "=== Testing Medium Cache Function ==="
log "First call to medium_cache_function(3, 4):"
let medium1 = medium_cache_function(3, 4)
log "Result: #{medium1}"

log "Second call to medium_cache_function(3, 4):"
let medium2 = medium_cache_function(3, 4)
log "Result: #{medium2}"

log "=== Cache Test Complete ==="
