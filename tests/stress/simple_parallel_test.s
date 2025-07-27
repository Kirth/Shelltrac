log "Testing parallel for with immutable state"

let numbers = 1..5

let results = parallel for i in numbers {
    return i * 2
}

log "Parallel for completed successfully"
log "First result should be 2"
log results[0]