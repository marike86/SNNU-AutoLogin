# objc2 license sources

These crates omit their repository's root license files from their crates.io
archives. Each `<crate>-<version>-LICENSE.md` is the upstream root `LICENSE.md`
at the exact Git revision recorded in that crate's `.cargo_vcs_info.json`.
Those declarations offer MIT for the core crates and MIT/Apache/Zlib for the
other framework crates. This distribution selects MIT.

The upstream MIT license text is reproduced in `objc2-MIT.txt`, retrieved from:
https://github.com/madsmtm/objc2/blob/8a5abacd90d5403fc0463459a35826f892b9142e/LICENSE-MIT.txt

The packaging script records each version's declaration URL in the generated
third-party notices and includes both the declaration and the MIT text.
