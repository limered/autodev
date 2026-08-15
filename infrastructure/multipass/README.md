# AI Software Factory — Multipass VM

Disposable Ubuntu LTS VM for running opencode jobs. One VM per job/feature.

## Files

- `cloud-init.yaml` — cloud-init configuration used by multipass to provision the VM.
- `smoke-test.sh` — verifies every required tool is installed and on PATH.

## Required tools

Install multipass on the Windows host: https://multipass.run/

## Launch a job VM

From the repo root:

```powershell
multipass launch 24.04 --name factory-job-01 `
  --cpus 4 --memory 8G --disk 40G `
  --cloud-init infrastructure\multipass\cloud-init.yaml
```

Use a unique name per job so multiple jobs can run in parallel.

## Run the smoke test

```powershell
multipass transfer infrastructure\multipass\smoke-test.sh factory-job-01:/tmp/smoke-test.sh
multipass exec factory-job-01 -- bash /tmp/smoke-test.sh
```

## Access the VM

```powershell
multipass shell factory-job-01
```

## Destroy the VM when done

```powershell
multipass delete factory-job-01
multipass purge
```

## Installed tools

- Git
- Docker
- opencode (`npm install -g opencode-ai`)
- .NET 8 SDK
- Node.js 20.x LTS
- SSH client

> **Note:** Godot 4 is intentionally omitted from the MVP image because downloads
> fail with TLS errors inside multipass VMs on this Windows host. It will be
> added once a reliable install path is found.
