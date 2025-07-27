# Test time literal functionality

log "=== Testing Time Literal Assignments ==="

# Test various time units
let milliseconds = 500ms
let seconds = 30s  
let minutes = 5m
let hours = 2h
let days = 1d

log "Milliseconds: #{milliseconds.Value}#{milliseconds.Unit} = #{milliseconds.TotalMilliseconds}ms"
log "Seconds: #{seconds.Value}#{seconds.Unit} = #{seconds.TotalMilliseconds}ms"
log "Minutes: #{minutes.Value}#{minutes.Unit} = #{minutes.TotalMilliseconds}ms"
log "Hours: #{hours.Value}#{hours.Unit} = #{hours.TotalMilliseconds}ms"
log "Days: #{days.Value}#{days.Unit} = #{days.TotalMilliseconds}ms"

# Test using time literals directly in expressions
log "=== Testing Time Literals in Expressions ==="
log "Direct usage: #{10s.TotalMilliseconds}ms"
log "Direct usage: #{2m.TotalMilliseconds}ms"

# Test in cache attributes with different time units
@cache(ttl: 100ms)
fn fast_cache(x) {
    log "Fast cache called with #{x}"
    return x
}

@cache(ttl: 1s)
fn slow_cache(x) {
    log "Slow cache called with #{x}"
    return x * 2
}

log "=== Testing Different Cache TTLs ==="
let fast1 = fast_cache(1)
let fast2 = fast_cache(1)  # Should use cache

let slow1 = slow_cache(2)
let slow2 = slow_cache(2)  # Should use cache

log "Fast cache results: #{fast1}, #{fast2}"
log "Slow cache results: #{slow1}, #{slow2}"