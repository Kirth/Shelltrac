
for machine in read_file("/home/kirth/LeafCloud/14022025_maas_machines.txt").Lines() {

  log machine
  let output = ssh "stack@#{machine}.maas" {
    docker ps | grep neutron_openvswitch_agent
  }

  log output
}
