
let instanceLogs = ssh "stack@machine.maas" { sudo docker exec nova_libvirt ls -ltrha /var/log/instances/ } parse as lstrha

for i in instanceLogs {
  let contains = ssh "stack@machine.maas" { 
    sudo docker exec nova_libvirt grep "run FSCK" ${{i}}
  } parse with fn(output) {
   # ...  
  }

  if contains {
    log "instance ${{i}} filesystem potentially tainted!"
  }
}
