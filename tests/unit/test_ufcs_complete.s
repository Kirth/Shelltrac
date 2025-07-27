fn add(a, b) {
    return a + b
}

fn multiply(a, factor) {
    return a * factor
}

fn square(n) {
    return n * n
}

log "=== UFCS Tests ==="

let result1 = add(5, 3)
log "Traditional: add(5, 3) = " + result1

let result2 = 5.add(3)
log "UFCS: 5.add(3) = " + result2

let result3 = 5.add(3).multiply(2)
log "Chained: 5.add(3).multiply(2) = " + result3

let result4 = 4.square()
log "Square: 4.square() = " + result4

let result5 = 2.add(3).square().multiply(5)
log "Complex chain: 2.add(3).square().multiply(5) = " + result5