fn filter(collection, pred) {
  let result = [ ] 
  for item in collection { 
    if pred(item) {
      result.Add(item)
    }
  }

  return result

} 

log 5 % 2 == 1 # make sure we actually have a modulo operator :3 

log [3, 4, 5].filter(fn(n) { return n % 2 == 0 })
log [3, 4, 5].filter(n -> n % 2 == 0)
