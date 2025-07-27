let json = sh {
  echo 'hello'
  echo 'world'
}  parse with {
  "setup": fn() { return 0 },
  "line": fn(line, count) {
    for c in line.ToCharArray() {
      if c == "l" { count = count + 1 }
    } 
    return count
  },
  "complete": fn(count) { return count }
}

log json
