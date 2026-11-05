# SPDX-License-Identifier: Apache-2.0
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
COPY --chmod=755 gatekeeper-ndpa-check /usr/local/bin/gatekeeper-ndpa-check
USER 1654:1654
ENTRYPOINT ["/usr/local/bin/gatekeeper-ndpa-check"]
