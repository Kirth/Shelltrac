log "Starting Shelltrac Performance Benchmarks"

log "Testing basic executor performance..."
let start_time = sh("date +%s%3N")

let simple_test = "Hello World"
log simple_test

let end_time = sh("date +%s%3N")
let duration = end_time - start_time
log "Basic test duration: #{duration}ms"

log "Testing variable operations..."
let test_var = 42
let lookup_result = test_var
log "Variable lookup works: #{lookup_result}"

log "Testing parallel execution..."
let numbers = 1..5
let results = parallel for i in numbers {
    return i * 2
}
log "Parallel execution completed"

log "Performance benchmark script completed successfully"