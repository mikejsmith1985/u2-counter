# Deploying

Two images and two local scripts. No hosted pipeline (Article VIII), so what
deploys is what was on the machine that deployed it, and the gate is the same
suite its author was running a minute earlier.

```
internet ──▶ counter-api      external ingress, HTTPS
                  │
                  ▼           private network only
             counter-mcp      internal ingress, one replica
                  │
                  ▼
             Azure SQL        the audit trail; no ERP data
```

## Once

```powershell
./azure/provision.ps1 -ResourceGroup counter-demo
```

Creates the registry, the Container Apps environment, a storage account and the
file share the audit trail lives on, and writes the names it chose to
`azure/environment.json`. `deploy.ps1` reads that file rather than deriving the
names again, because a random suffix derived in two places eventually disagrees.

There is no database server and no database password, because the audit trail is
a SQLite file on that share.

**Where this falls short of Article IX.** Two credentials do pass through these
scripts: the storage account key, to attach the share to the environment, and the
registry password, to let the apps pull. Both are read from `az` into a variable
and passed on a command line. Neither is committed, and both are short-lived
enough to rotate — but the Article says the agent names where a secret goes and
something else delivers it, and that is not what happens here. Closing it means a
user-assigned managed identity for the registry pull and a Key Vault reference
for the share key; it is a known gap, not an oversight.

## Each release

```powershell
./azure/deploy.ps1
```

Runs every suite first and refuses to deploy if any fails. Tags images with the
short commit hash, marked `-dirty` when the working tree has uncommitted changes,
because a hash naming code that is not what shipped is worse than no tag.

Images are built by `az acr build` in the registry rather than locally and
pushed, which removes "it built on my machine" as an explanation for a difference
between here and there.

## Two decisions worth knowing about

**The MCP server has no public address.** It holds the database session and
enforces the read-only rules. A public address would put those rules between the
internet and a Universe account, and a rule enforced in one place has exactly one
way to be wrong.

**It runs one replica, deliberately.** It holds a single database session, and a
second replica would hold a second — which is the connection multiplication the
hardened fork was fixed for, reintroduced by the deployment rather than by the
code.
