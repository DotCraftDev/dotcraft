#!/bin/bash
# DotCraft start script from source

export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools

cd ../../
dotnet build

cd samples/workspace
dotnet ../../src/DotCraft.App/bin/Debug/net10.0/dotcraft.dll