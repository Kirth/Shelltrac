fn greet_somehow(how, who) { log "#{how} #{who}" } 

fn greet_gen(how) {
  return fn(who) {
    greet_somehow(how, who)
  }
}

let hithere = greet_gen("hi!")
hithere("kirth")
