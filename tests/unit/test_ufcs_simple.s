fn add(a, b) {
    return a + b;
}

let result1 = add(5, 3);
log "Traditional: #{result1}";

let result2 = 5.add(3);
log "UFCS: #{result2}";