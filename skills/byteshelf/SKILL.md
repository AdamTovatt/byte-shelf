---
name: byteshelf
description: Store, find, read and share files on a ByteShelf server with the byteshelf command line. Use when asked to save files or results somewhere lasting, find a file kept on a shelf, fetch one, back up a folder, or give someone a link to a file.
---

# ByteShelf from the command line

ByteShelf is a self-hosted file store. Files live on **shelves**: each shelf is its own tree of
directories and files with its own trash and quota. `byteshelf` is its command line, a dotnet tool.

## Getting set up

```bash
dotnet tool install -g ByteShelf.Cli      # once
export BYTESHELF_URL=https://files.example.com/
export BYTESHELF_KEY=bsk_...              # a shelf key from the shelf's settings in the web UI
byteshelf whoami                          # the server and the shelf the key reaches
```

A key reaches one shelf, with one permission: `read`, `add-only` (read, and store new files, never
replace, move or delete) or `read-write`. Never print the key or put it on a command line; keep it in
the environment. A person can instead run `byteshelf login <server> --email <email>` once.

`byteshelf help` lists every command with its options.

## Places and paths

A place is written `shelf:/path`; `shelf` alone is its root. Paths start with `/`. A path ending in
`/` names a directory, so `put report.pdf docs:/2026/` keeps the file's name. Quote a path with
spaces.

## Doing things

| To | Run |
|---|---|
| See which shelves you reach | `byteshelf shelves`, and `byteshelf whoami` for who you act as |
| See what is there | `byteshelf ls docs:/2026` or, everything below, `byteshelf tree docs:/2026 --depth 2` |
| Find files | `byteshelf find docs invoice --type application/pdf --created-from 2026-03-01` |
| Read a file's record | `byteshelf stat docs:/2026/report.pdf` |
| Store a file | `byteshelf put report.pdf docs:/2026/ --description "Q1 report, final"` |
| Store text from a program | `some-command \| byteshelf put - docs:/logs/run.txt` |
| Save a file | `byteshelf get docs:/2026/report.pdf ./report.pdf` (or `-` for stdout) |
| Back up or restore a folder | `byteshelf put ./site docs:/backups/` and `byteshelf get docs:/backups/site ./site-copy` |
| Give someone a link | `byteshelf share docs:/2026/report.pdf --expires 2d` |
| Say what a file is | `byteshelf describe docs:/2026/report.pdf "Signed by both parties"` |
| Make a directory | `byteshelf mkdir docs:/2026/q2` (missing parents too) |
| Move or rename | `byteshelf mv docs:/2026/draft.pdf /2026/final.pdf` (the new path may repeat the shelf, `docs:/2026/final.pdf`; a name holding a colon starts with `/`, as in `/notes:2026.txt`) |
| Delete (to the trash) | `byteshelf rm docs:/2026/old.pdf`, then `byteshelf trash docs` and `byteshelf restore docs <id>` |
| Empty the trash for good | `byteshelf empty-trash docs` (nothing in it can come back afterwards) |

- `find` matches text in a file's name or its description, ignoring case, so describe what you store.
  Quote a type family: `--type 'image/*'`.
- `tree` and `find` answer a page at a time. A page that has more ends with
  `More: add --after '<cursor>'`; run the same command with that option for the next page.
- `put` refuses a path that is taken; add `--overwrite` to replace the file (it goes to the trash, so
  it can come back). `get` refuses to replace a local file without `--overwrite` too.
- `get` checks every file against its SHA-256; saving to a local file, a mismatch leaves nothing
  behind. `get … -` writes to stdout as the bytes arrive, so a mismatch fails the command (exit code
  1) only after they have gone out: check the exit code before using what was piped.
- `share` prints the link alone on its first line. Anyone who has it can open the file until it runs
  out (24 hours by default, at most 7 days), and nothing takes it back sooner, so share only what may
  be seen. It refuses a file encrypted with a password `(conflict)`.
- A file or folder can be encrypted with a password (`ls` and `stat` mark it; `byteshelf encrypt` and
  `decrypt` change it). Without the password, `get` saves such a file as the age file it is stored
  as: to `./report.pdf` or `-` those are age bytes, not a PDF, and into a folder it is named
  `report.pdf.age`. Add `--password-file <file>` (its first line is the password) to decrypt it; `put`
  under an encrypted folder needs that folder's password the same way. Never put a password on a
  command line, and only use one a person gave you.
- Add `--json` to get the server's JSON for anything you parse.

## When something fails

The exit code says what kind of failure it was:

- `0`: done.
- `1`: the server refused or could not be reached, a download did not match its hash, a local file
  could not be read or written, an encrypted file is damaged, a folder transfer left something out (listed in the output), or
  another `byteshelf` command held the config file for 30 seconds (run it again).
- `2`: the command was given wrongly, or cannot run as things are (not signed in, a missing local
  file, a local file in the way without `--overwrite`, a wrong password).
- `130`: interrupted with Ctrl+C.

A refusal ends with its code in parentheses: `(not_found)`, `(conflict)` for a path that is taken,
`(permission_denied)` for something the key may not do, `(quota_exceeded)` when the shelf is full.

## As an MCP server

`byteshelf --mcp` serves the same operations as MCP tools over stdio, and the server itself serves
them at `/mcp` for a key. Prefer the tools when they are connected; this skill is for when only a
shell is.

## Installing this skill

For Claude Code, copy this folder (`skills/byteshelf` in the ByteShelf repository) to
`~/.claude/skills/byteshelf/` for every project, or to `.claude/skills/byteshelf/` in one project.
