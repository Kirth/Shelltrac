
fn mod(a, b) { return a - (a/b) * b }
let nums = [1, 2, 3, 4, 5, 6]
#let doubles = nums.
#  Where(x -> x - 2 == 0).
#j  Select(x -> x * 2).
 # ToList();
#log doubles

fn filter(collection, with) {
  let res = [];
  for i in collection {
    if with(i) {
      res.Add(i)
    }
  }

  return res
}

let client = instantiate("System.Net.Http.HttpClient");
let resp = client.GetStringAsync("http://kirth.be").Result;
let ks = filter(resp, c -> c == "k").Count;
log ks




let y = filter([1, 2, 3, 4, 5, 6], n -> mod(n, 2) == 0)

let a = ["abra", "kadabra", "alakazam"]
let b = filter(a, fn(s) { return s.StartsWith("a") })

let anon = fn(x) { return x * 2; }

log anon(300)

log b

log y

let name = "Jean-Luc"
let foo = sh {
  # Declare a variable
  name="#{name}"

  # Echo it
  echo "Hello, $name"
  echo "Hi \"#{name.ToUpper()}\""
}

log foo
