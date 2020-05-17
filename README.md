# RaftMod.FilteredNets

FilteredNets is a Raft-Mod, that allows you to configure your item nets to only catch specific items

https://www.raftmodding.com/mods/filterednets

## Features:
 
Use the Rotate-Key (default is R) on an item net to select a specific filter mode.

**Supported filter modes:**

- Default: Every item will be caught by the item net.
- Planks: Planks and floating barrels will be caught by the item net.
- Plastic: Plastic and floating barrels will be caught by the item net.
- Thatches: Thatches and floating barrels will be caught by the item net.
- Barrels: Only floating barrels will be caught by the item net.

## Under development

This mod currently under development. 

## Changes

### 1.23

- In some cases, the configuration file was corrupted due to bad file handling.

### 1.22

- In some cases, the GetLocalPlayer was used to early and resulted in an NullReferenceException

### 1.21

- Added a few null-checks

### 1.2

- Multiplayer Support
