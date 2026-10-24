# SQL Server 2025 with the Full-Text component (mssql-server-fts), which the official image does not carry: CONTAINS, FREETEXT and
# full-text indexes. Built by the tests' fixture (Testcontainers) and cached under the tag it gives.
FROM mcr.microsoft.com/mssql/server:2025-latest
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl gnupg ca-certificates \
 && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --batch --yes --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
 && curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/mssql-server-2025.list -o /etc/apt/sources.list.d/mssql-server-2025.list \
 && apt-get update \
 && apt-get install -y --no-install-recommends mssql-server-fts \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*
USER mssql
