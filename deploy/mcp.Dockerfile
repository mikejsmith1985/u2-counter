# The hardened MCP server, with the demonstration store behind it.
#
# A separate image from the API on purpose. This is the process that holds the
# database session and enforces the read-only rules, and the boundary between it
# and the application is what makes those rules a property of the deployment
# rather than of the application's own good behaviour. Merged into one container
# they would be a matter of configuration, and configuration drifts.
#
# It carries the fork, not the upstream package. Every fix in docs/hardening.md
# lives here, and installing u2-mcp from an index would quietly deploy the
# version those fixes were written against.

FROM python:3.12-slim AS runtime

# Built as a non-root user with no shell. This server's worst historical failure
# was a shell escape through a query verb; the fix is in the code, and a
# container whose user cannot open a shell anyway is the second answer to the
# same question.
RUN useradd --create-home --shell /usr/sbin/nologin --uid 10001 u2mcp

WORKDIR /srv

# The fork's own source. Copied and installed rather than pulled from an index,
# because the point of this image is that it runs the hardened version.
COPY u2-mcp/pyproject.toml u2-mcp/README.md ./u2-mcp/
COPY u2-mcp/src/ ./u2-mcp/src/

# The demonstration store, which the server loads as a driver rather than
# reaching over a network. It presents the objects uopy presents, so the server
# runs the code it would run against a real Universe.
COPY counter/mvstore/pyproject.toml counter/mvstore/README.md ./mvstore/
COPY counter/mvstore/src/ ./mvstore/src/
COPY counter/mvstore/data/ ./data/

RUN pip install --no-cache-dir ./u2-mcp ./mvstore

USER u2mcp

# The demonstration driver. Setting this to uopy is what makes the image talk to
# a real database, and nothing else here changes.
ENV U2_DRIVER=demo \
    MVSTORE_DATA_PATH=/srv/data \
    PYTHONUNBUFFERED=1

# Present because the server validates its connection settings at startup
# whichever driver is loaded. With the demo driver nothing authenticates against
# them, and there is no database behind them. A real deployment overrides all of
# them from a secret store and never from here.
#
# U2_PASSWORD is deliberately absent: a password baked into an image layer is a
# password in the registry, readable by anyone who can pull. The container will
# refuse to start without one, which is the correct failure -- it says plainly
# that the deployment forgot to supply a credential rather than starting with a
# default nobody chose. deploy/azure/deploy.ps1 passes it as a secret.
ENV U2_HOST=127.0.0.1 \
    U2_USER=u2demo \
    U2_ACCOUNT=DEMO

EXPOSE 5081

# Bound to every interface inside the container so the platform can reach it.
# What keeps this private is that the container app is deployed with internal
# ingress only -- see deploy/azure/provision.ps1. The server itself refuses to
# serve unauthenticated traffic on a reachable interface, which is the check that
# does not depend on anyone configuring ingress correctly.
ENTRYPOINT ["u2-mcp", "--streamable-http", "--host", "0.0.0.0", "--port", "5081"]
