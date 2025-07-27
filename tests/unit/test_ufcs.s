fn add(a, b) {
    return a + b;
}

fn multiply(a, factor) {
    return a * factor;
}

let result1 = add(5, 3);
log "Traditional call: add(5, 3) = #{result1}";

let result2 = 5.add(3);
log "UFCS call: 5.add(3) = #{result2}";

let result3 = 5.add(3).multiply(2);
log "Chained UFCS: 5.add(3).multiply(2) = #{result3}";