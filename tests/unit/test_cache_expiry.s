# Test cache expiry functionality

@cache(ttl: 1s)
fn short_cache_test(x) {
    log "Function executed with #{x}"
    return x * 100
}

log "=== Testing Cache Expiry ==="

# First call
log "First call:"
let result1 = short_cache_test(5)
log "Result: #{result1}"

# Second call immediately - should use cache
log "Second call (immediate):"
let result2 = short_cache_test(5)
log "Result: #{result2}"

# Wait for cache to expire (simulated by multiple calls to ensure some time passes)
log "Waiting for cache to expire..."
for i in 1..1000 {
    # Just some busy work to let time pass
    let dummy = i * 2
}

# Call after expiry - should execute again
log "Call after expiry:"
let result3 = short_cache_test(5)
log "Result: #{result3}"