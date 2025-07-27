#let json = (sh "cat '/home/kirth/Computer/Shelltrac/obj/project.assets.json'").ParseJson()
let json = sh  {
  ls -ltrha / | grep h
}  parse with {
  "setup": fn() { return 0 },
  "line": fn(line, count) {
      for c in line.ToCharArray() {
        if c == "r" { count = count + 1 }
      } 
      return count
    },
    "complete": fn(count) { return count }
  }

log json
