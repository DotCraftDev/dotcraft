#!/bin/bash
# DotCraft start script from source

export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools

cd ..
dotnet build

cd Workspace
dotnet ../DotCraft/bin/Debug/net10.0/DotCraft.dll
