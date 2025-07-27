fn is_vip(hostStr) {
  let ifaces = ssh "stack@#{hostStr}" "ip -4 -br a"
  return ifaces.Stdout.Contains("10.10.39.254") && ifaces.Stdout.Contains("195.114.30.254")
}

let controllers = ["ams1-dc-controller-1.maas", "ams1-dc-controller-3.maas", "ams1-dc-controller-2.maas"]
for c in controllers { 
  log "testing controller #{c}"
  if is_vip(c) { log "#{c} is VIP" } }
