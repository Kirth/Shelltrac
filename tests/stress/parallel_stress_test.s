let start_time = sh("date +%s%3N")

let large_range = 1..1000

log "Starting parallel stress test with 1000 iterations"

let results = parallel for i in large_range {
    let computed = i * 2 + 1
    return computed
}

let end_time = sh("date +%s%3N")
let duration = end_time - start_time

log "Parallel stress test completed"
log "Results count: #{results.length}"
log "Duration: #{duration}ms"

if results.length == 1000 {
    log "✓ Correct number of results"
} else {
    log "✗ Expected 1000 results, got #{results.length}"
}

if results[0] == 3 && results[1] == 5 && results[2] == 7 {
    log "✓ Results are correct"
} else {
    log "✗ Results are incorrect: #{results[0]}, #{results[1]}, #{results[2]}"
}