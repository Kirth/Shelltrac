let boxes = sh {
  nix-shell ~/LeafCloud/shell.nix --run "source ~/LeafCloud/proadm/app-cred-kirth-test-openrc.sh; cd ~/LeafCloud/INCI-1; terraform output -json instance_ips";
}


let boxes = boxes[0].FromJson()

for box in boxes {
  let x = sh "rsync -razpe 'ssh -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null' ubuntu@#{box.Value}:/data/results ~/INCI-1/results_2204/writethrough/results-#{box.Key}"
  log x
}
