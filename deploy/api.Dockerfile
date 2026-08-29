# The API, serving the front end it was built beside.
#
# One image for both, because the browser then only ever talks to one origin:
# no cross-origin configuration to get wrong, and a session cookie that is
# simply first-party. Two images would mean two things to deploy and a CORS
# policy to keep correct between them, for no benefit anyone using it would see.
#
# The MCP server is a separate image (mcp.Dockerfile) and reached over the
# private network. That separation is not tidiness: it is the boundary the
# read-only guarantee lives on, and putting the two in one container would make
# it a matter of configuration rather than of network.

# -- build the front end ------------------------------------------------------
# Pinned to a major version, not to a digest.
#
# A digest is what would make a rebuild reproducible, and these tags float within
# their major: the same Dockerfile can produce a different image next month. That
# is a real gap and it is stated rather than papered over -- pinning digests here
# means a renovation job to move them, which this demonstration does not have.
FROM node:24-alpine AS web

WORKDIR /src/web

# Dependencies are installed from the lockfile alone, before the source is
# copied, so a change to a component does not re-run the install. `npm ci` fails
# rather than resolving a version the lockfile did not name.
COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
RUN npm run build

# -- build the API ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api

WORKDIR /src

COPY api/Counter.sln ./
COPY api/src/ ./src/
COPY api/tests/ ./tests/

RUN dotnet restore Counter.sln

# Published rather than built: a self-contained output with no SDK behind it,
# which is most of the difference between a 200MB image and an 800MB one.
RUN dotnet publish src/Counter.Api/Counter.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# -- the image that runs ------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Not root. A read-only application has no reason to hold write access to its own
# filesystem, and the ASP.NET images ship a non-root user for exactly this.
USER $APP_UID

WORKDIR /app

COPY --from=api /app ./
COPY --from=web /src/web/dist ./wwwroot

# Bound to every interface inside the container, which is the only way the
# container platform can reach it. What keeps this private is the ingress
# configuration, not the bind address.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# No HEALTHCHECK here. The probe belongs to whatever runs the container, and
# Container Apps polls /health over HTTP -- which is the check that matters,
# because a process can be alive while the catalogue it needs is still empty. A
# second check baked into the image would be one more thing to keep in step.

ENTRYPOINT ["dotnet", "Counter.Api.dll"]
