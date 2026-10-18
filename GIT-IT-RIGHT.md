# git-it-right

This repository uses [gir (git-it-right)](https://github.com/flatrick/git-it-right) to keep commit messages consistent.
gir checks every commit message against [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/), fixes safe mistakes for you, and explains anything it rejects.
Consistent messages make the history readable and let tools build the changelog and the next version number from it.

## Contributing: once per clone

1. Install gir: see [Install](https://github.com/flatrick/git-it-right#install).
2. In your clone, run `gir init`.

Git never turns on hooks from a fresh clone, so step 2 is needed in every clone.
It sets `core.hooksPath` to `.githooks` and restores the hooks and `.girconfig` if they are missing.

## Writing commits

Write messages as `<type>[(scope)][!]: <description>`, for example `fix(api): return 404 for unknown users`.

- `gir explain types` lists the types this repository allows (they are set in `.girconfig`).
- When a commit is rejected, the message names a rule in brackets, such as `[type-unknown]`; `gir explain <rule>` explains it.
- `gir fixup` and `gir explain fixup` cover fixing an earlier commit on your branch.

## Maintainers: checking contributions

Without CI, check a contributor's branch before you merge it:

```sh
gir lint --range main..<their-branch>
```

With CI, run the same check on every pull request:

```sh
gir lint --range origin/main..HEAD
```

This also catches commits made with `git commit --no-verify`.

## Changelog (optional)

`gir init` also writes `cliff.toml`, a configuration for [git-cliff](https://git-cliff.org) that matches this repository's commit types.
Contributors do not need it; it is for whoever cuts a release:

```sh
git-cliff -o CHANGELOG.md     # write the changelog
git-cliff --bumped-version    # print the next version
```

gir works without it.
If you do not want it, delete `cliff.toml` and commit the deletion; `gir init` will not add it back.

## This file

`gir init` wrote this file.
Edit it, or delete it and commit the deletion; `gir init` will not add it back.
`gir init --optional` restores a deleted `cliff.toml` or `GIT-IT-RIGHT.md` without touching files you changed.
