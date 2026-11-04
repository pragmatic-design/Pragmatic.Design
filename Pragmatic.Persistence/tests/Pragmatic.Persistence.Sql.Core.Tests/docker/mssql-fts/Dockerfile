# SQL Server 2025 with the Full-Text component (mssql-server-fts), which the official image does not carry: CONTAINS, FREETEXT and
# full-text indexes. Built by the tests' fixture (Testcontainers) and cached under the tag it gives.
#
# Pinned, both halves to one version: the base by digest (2025-latest as of 17.0.5035.5, its com.microsoft.version label) and the
# full-text package to that version. mssql-server-fts depends on the mssql-server package, which the image does not register with dpkg,
# so apt installs the server too: unpinned, a floating base and the repository's newest packages could put two releases in one image,
# and the image CI builds would not be the image that was tested. To move to a newer release, change the digest and the version together.
FROM mcr.microsoft.com/mssql/server:2025-latest@sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3
ARG MSSQL_VERSION=17.0.5035.5-3
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl gnupg ca-certificates \
 && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --batch --yes --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
 && curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/mssql-server-2025.list -o /etc/apt/sources.list.d/mssql-server-2025.list \
 && apt-get update \
 && apt-get install -y --no-install-recommends mssql-server=${MSSQL_VERSION} mssql-server-fts=${MSSQL_VERSION} \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*
USER mssql
