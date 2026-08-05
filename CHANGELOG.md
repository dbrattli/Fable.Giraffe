---
last_commit_released: 2409ef004be987ec43eb6692c8e2b8318faff8a3
name: Fable.Giraffe
---

# Changelog

All notable changes to this project will be documented in this file.

## 5.3.0 - 2026-08-04

### 🏗️ Breaking changes

* OpenAPI 3.1 support, an opt-in endpoint layer, and typed JSON via Fable.TypedJson (#74) ([9528f3a](https://github.com/dbrattli/Fable.Giraffe/commit/9528f3ad38abe9cd2b2b0b2630abc5b4a8510bf9))

### 🚀 Features

* *(beam)* Enable logging and add a per-request access log (#70) ([e9a5fb4](https://github.com/dbrattli/Fable.Giraffe/commit/e9a5fb4479d7c39ed4fdcb1fc584e9f00ff33611))

### 🐞 Bug Fixes

* *(beam)* Make DI work across Cowboy's per-request process (#71) ([f6d3bbc](https://github.com/dbrattli/Fable.Giraffe/commit/f6d3bbcc4965d9382697da5fb03221d7b2411eaa))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.Giraffe/compare/4020946b153b18572b4aa37e699ee35c0bdd41d0..2409ef004be987ec43eb6692c8e2b8318faff8a3)</small></strong>

## 5.2.0 - 2026-07-26

### 🚀 Features

* Add reproducible cross-target benchmark harness (#58) ([cea0f45](https://github.com/dbrattli/Fable.Giraffe/commit/cea0f4500b78f2664bcd5baf78d3fe97aa9fc76d))

### ⚡ Performance Improvements

* Trim per-request overhead on the Python hot path (#60) ([567cf9f](https://github.com/dbrattli/Fable.Giraffe/commit/567cf9fa7748fedeae364458c4fbe67e7c2a9b44))
* Compose the BEAM handler pipeline once, not per request (#61) ([99487ad](https://github.com/dbrattli/Fable.Giraffe/commit/99487ad7b5b878b20a18d4704dfbe3fba530a415))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.Giraffe/compare/28b9388483892e016dbafa08c902ced2197e0dd7..4020946b153b18572b4aa37e699ee35c0bdd41d0)</small></strong>

## 5.1.0 - 2026-07-25

### 🚀 Features

* Enable remoting on the BEAM target (#55) ([9ec1c7a](https://github.com/dbrattli/Fable.Giraffe/commit/9ec1c7a2834855cdda2c1c25980a4f6f00f26006))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.Giraffe/compare/ad0bf743fc468b0f95b8ddd45001ab65c593a724..28b9388483892e016dbafa08c902ced2197e0dd7)</small></strong>

## 5.0.1 - 2026-07-24

### 🐞 Bug Fixes

* Restore a real project in the release job and add a manual trigger (#52) ([ad0bf74](https://github.com/dbrattli/Fable.Giraffe/commit/ad0bf743fc468b0f95b8ddd45001ab65c593a724))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.Giraffe/compare/79eba87897ec73d3783f02a2540e47d598a308fc..ad0bf743fc468b0f95b8ddd45001ab65c593a724)</small></strong>

## 5.0.0 - 2026-07-23

### 🚀 Features

* Adopt Fable 5 and start the 5.0.0 release line (#46) ([6d14935](https://github.com/dbrattli/Fable.Giraffe/commit/6d14935d75e5f37659c0ed29df34f3e4c51b2716))
* Add shared htmlFile handler across all three backends (#48) ([cc08455](https://github.com/dbrattli/Fable.Giraffe/commit/cc08455045c243630a1d4ad92ca14e8fdd3f12a7))
* Add UseStaticFiles for the BEAM and JS backends (#49) ([c0bb61c](https://github.com/dbrattli/Fable.Giraffe/commit/c0bb61cf326236b56752890d964154ec38788eb5))
* Add prefix-mount UseStaticFiles form to the Python backend (#50) ([30ced97](https://github.com/dbrattli/Fable.Giraffe/commit/30ced970cd5e36653590de4e00fc4bf857270629))
* Share the remoting handler, enable it on JS, and harden it (#51) ([79eba87](https://github.com/dbrattli/Fable.Giraffe/commit/79eba87897ec73d3783f02a2540e47d598a308fc))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.Giraffe/compare/8f3057f04837d2b44ef72f2a225fff0050e62f19..79eba87897ec73d3783f02a2540e47d598a308fc)</small></strong>

## 0.12.0 - 2023-11-28

Baseline release. Entries above this line are generated automatically by
[EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt) from
[Conventional Commit](https://www.conventionalcommits.org/) messages.
