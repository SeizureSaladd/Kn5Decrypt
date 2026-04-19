# Kn5Decrypt
---
`Kn5Decrypt` is a simple terminal application for working with Assetto Corsa `KN5` and `data.acd` files.

It currently focuses on three jobs:

- Decrypt CSP-protected `KN5` files.
- Unpack and decrypt `data.acd` archives into plain-text files
- Remove simple `KN5` unpack protection.

## Intent and Disclaimer

This project was mainly a reverse-engineering exercise to better understand `KN5`, `data.acd`, and the protection/encryption schemes around them.

Only use it on content you own, or on content you have explicit permission to inspect, decrypt, modify, or restore.

## What It Does

### `decrypt`

Reads a CSP-protected `KN5`, extracts the encrypted sidecar data, resolves the per-car salt, decrypts textures and vertex masks, and tries to rebuild a plaintext `KN5`.

When a full rebuild is possible, the tool writes:

- A rebuilt `*_decrypted.kn5`
- A `textures/` folder with recovered texture payloads
- A `vertex_masks/` folder with recovered mesh masks
- A `vertex_masks/manifest.txt` summary file

When a full rebuild is not possible, it still writes the decrypted body-only file so you can inspect the recovered data.

### `acd`

Unpacks and decrypts Assetto Corsa `data.acd` archives. The key derivation matches the original behavior, so the folder or file name where the archive is matters.

### `unprotect`

Detects and removes the simple `KN5` unpack protection that disables the "Unpack LODs" button in Custom Showroom. This command patches the file in place and writes a `.bak` backup first.

## Requirements

- .NET SDK 9.0 or newer that can build `net9.0`
- Tested on Windows

## Build

```powershell
dotnet build .\Kn5Decrypt\Kn5Decrypt.csproj
```

## Run

Show help:

```powershell
./Kn5Decrypt --help
```

Open the interactive menu:

```powershell
./Kn5Decrypt
```

## Command Reference

### Decrypt a CSP-protected KN5

```powershell
./Kn5Decrypt decrypt <file.kn5> [outDir]
```

If `outDir` is omitted, the tool writes to `<file name>_decrypted` next to the source `KN5`.

Example:

```powershell
./Kn5Decrypt decrypt "C:\cars\my_car\car.kn5"
```

### Unpack a `data.acd`

```powershell
./Kn5Decrypt acd <data.acd> <outDir>
```

Example:

```powershell
./Kn5Decrypt acd "C:\cars\my_car\data.acd" "C:\cars\my_car\acd_out"
```

### Remove KN5 unpack protection

```powershell
./Kn5Decrypt unprotect <file.kn5>
```

Example:

```powershell
./Kn5Decrypt unprotect "C:\cars\my_car\lods.kn5"
```

## Output Notes

- `decrypt` creates `textures/` and `vertex_masks/` subfolders under the chosen output directory.
- `unprotect` always writes a `.bak` file as a backup before touching the original file.

## Limitations

- `decrypt` only supports the CSP `__AC_SHADERS_PATCH_KN5ENC_v1__` envelope.
- `encVersion > 3` is currently unsupported.
- Full `KN5` rebuild is currently implemented for `encVersion == 1`.
- For newer supported envelope versions, or when body parsing fails, the tool writes a decrypted body-only `KN5` instead of a full rebuilt file.
- A body-only fallback can still be useful for inspection, but the geometry will be incorrect.

## Safety

Use this on files you own or are explicitly allowed to inspect and modify. `unprotect` changes files in place, so keep the generated `.bak` if you may want to roll back.
