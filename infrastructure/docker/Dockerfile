FROM ubuntu:24.04

# Mirrors infrastructure/multipass/cloud-init.yaml so container Jobs start
# from the same toolchain as VM Jobs without the 7-minute provision.
# Build once: podman build -t slop-factory-runner:latest infrastructure/docker

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates \
      curl \
      docker.io \
      git \
      gnupg \
      jq \
      openssh-client \
      tar \
      unzip \
      wget \
 && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
 && apt-get install -y --no-install-recommends nodejs \
 && npm install -g opencode-ai \
 && wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb \
 && dpkg -i /tmp/packages-microsoft-prod.deb \
 && apt-get update \
 && apt-get install -y --no-install-recommends dotnet-sdk-8.0 \
 && rm -f /tmp/packages-microsoft-prod.deb \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

# Godot 4 stays deferred, same as cloud-init: no reliable install path yet.
