# ADR 004: Linux runner uses ephemeral containers, not VMs

On Nobara the host is already Linux with native Docker, so the Windows reasons for a per-Job multipass VM (Linux env + Docker + port isolation, ADR-001) disappear and the 7-minute cloud-init becomes pure overhead. We run each Job in an ephemeral Docker container from a prebuilt image carrying the same toolchain, reusing the existing queue, heartbeat/phase/freeze contracts via a host-mounted signal dir. Rejected: host-direct exec (an agent can touch the desktop) and multipass-KVM on Linux (parity at VM cost).
