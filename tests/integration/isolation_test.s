log "Testing variable isolation in parallel execution"

let shared_var = 100

let numbers = 1..3

log "Before parallel execution, shared_var = #{shared_var}"

let results = parallel for i in numbers {
    let local_var = shared_var + i
    return local_var
}

log "After parallel execution, shared_var = #{shared_var}"
log "Results (should be around 101, 102, 103 in some order): #{results}"