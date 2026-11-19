# SQL Server 2025 with the Full-Text component (mssql-server-fts), which the official image does not carry: CONTAINS, FREETEXT and
# full-text indexes. Built by the tests' fixture (Testcontainers) and cached under the tag it gives.
#
# Pinned, both halves to one version: the base by digest (2025-latest as of 17.0.5035.5, its com.microsoft.version label) and the
# full-text package to that version. mssql-server-fts depends on the mssql-server package, which the image does not register with dpkg,
# so apt installs the server too: unpinned, a floating base and the repository's newest packages could put two releases in one image,
# and the image CI builds would not be the image that was tested. To move to a newer release, change the digest and the version together.
#
# Nothing comes from the Ubuntu archive (#893). apt reads one source, Microsoft's repository, through a sources directory of its own: the
# archive's sources stay in the image and are not read. The key is added in its armored form, which apt reads as is, so neither curl nor
# gnupg has to be installed first; those were the only packages the archive gave. A build then depends on packages.microsoft.com alone,
# and a fetch is retried before the build gives up.
FROM mcr.microsoft.com/mssql/server:2025-latest@sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3
ARG MSSQL_VERSION=17.0.5035.5-3
USER root
ADD https://packages.microsoft.com/keys/microsoft.asc /etc/apt/keyrings/microsoft.asc
RUN chmod 644 /etc/apt/keyrings/microsoft.asc \
 && mkdir -p /etc/apt/mssql-sources \
 && echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/microsoft.asc] https://packages.microsoft.com/ubuntu/24.04/mssql-server-2025 noble main" \
      > /etc/apt/mssql-sources/mssql-server-2025.list \
 && apt-get -o Acquire::Retries=5 -o Dir::Etc::SourceList=/dev/null -o Dir::Etc::SourceParts=/etc/apt/mssql-sources update \
 && apt-get -o Acquire::Retries=5 -o Dir::Etc::SourceList=/dev/null -o Dir::Etc::SourceParts=/etc/apt/mssql-sources \
      install -y --no-install-recommends mssql-server=${MSSQL_VERSION} mssql-server-fts=${MSSQL_VERSION} \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*
USER mssql
