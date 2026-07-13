#!/usr/bin/env bash
set -euo pipefail

sudo apt-get update
sudo apt-get install -y ffmpeg

dotnet restore
sudo dotnet workload update
dotnet dev-certs https --trust
