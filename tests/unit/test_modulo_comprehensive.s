log "=== Modulo Operator Tests ==="

let result1 = 10 % 3
log "10 % 3 = " + result1

let result2 = 15 % 4
log "15 % 4 = " + result2

let result3 = 20 % 5
log "20 % 5 = " + result3

let result4 = 7 % 2
log "7 % 2 = " + result4

let result5 = 100 % 7
log "100 % 7 = " + result5

let result6 = 1 % 5
log "1 % 5 = " + result6

fn isEven(n) {
    return n % 2 == 0
}

let test1 = isEven(4)
let test2 = isEven(5)
log "isEven(4) = " + test1
log "isEven(5) = " + test2

let precedence1 = 10 + 3 % 2
log "10 + 3 % 2 = " + precedence1

let precedence2 = (10 + 3) % 2  
log "(10 + 3) % 2 = " + precedence2