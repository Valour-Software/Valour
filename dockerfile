# Start with the official .NET 11 preview SDK image
# Cache the dependencies so we don't have to restore them every time
#
# --platform=$BUILDPLATFORM: the build stages always run natively on the build
# host and cross-publish for $TARGETARCH (see the publish step below). Emulating
# the full .NET publish under QEMU is slow and unreliable, so multi-arch images
# only emulate the final runtime stage, which runs nothing at build time.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0.100-preview.3 AS dependencies

# Install Node.js (replace with the latest LTS version)
RUN curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
    && apt-get install -y nodejs

# Set the working directory to the app's source code directory
WORKDIR /app

# We need python for some reason
RUN apt-get update && apt-get install -y python3
RUN apt-get install -y libatomic1

# Restore the .NET dependencies
FROM dependencies AS dotnet-restore

# Copy the app's source code to the container image
COPY . .

# Stamp the $(SHORTHASH) asset-version placeholders (index.html, Version.cs, ...).
# CI passes the git short hash; local source builds default to "dev" because .git
# is dockerignored and the hash cannot be derived here. A no-op when the source
# was already stamped (as in CI, which replaces placeholders before docker build).
ARG SHORTHASH=dev
RUN grep -RIlZ '\$(SHORTHASH)' Valour | xargs -0 -r perl -pi -e 's/\$\(SHORTHASH\)/$ENV{SHORTHASH}/g'

# Restore workloads required by the server publish graph
RUN dotnet workload restore Valour/Server/Valour.Server.csproj

# Restore the app's dependencies
RUN dotnet restore Valour/BuildTools/CssBundler/CssBundler.csproj
RUN dotnet restore Valour/Server/Valour.Server.csproj

# Build stage for building/publishing the app
FROM dotnet-restore AS build

# Remove .js files that have corresponding .ts files
# RUN find . -name "*.ts" | while read tsfile; do \
#        jsfile="${tsfile%.ts}.js"; \
#        if [ -f "$jsfile" ]; then \
#            echo "Deleting $jsfile because $tsfile exists"; \
#            rm "$jsfile"; \
#        fi; \
#    done

# Build the app for the image's target architecture (framework-dependent,
# RID-specific). TARGETARCH is amd64/arm64 from buildx; the .NET CLI accepts
# those aliases directly.
ARG TARGETARCH
RUN dotnet publish Valour/Server/Valour.Server.csproj -c Release -a $TARGETARCH -o out

# Staging dir for the media mount point (see COPY --chown below)
RUN mkdir -p /staging/media

# Start with a smaller runtime image for the final image
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-preview.3-resolute-chiseled-extra AS final

# Set the working directory to the app's output directory
WORKDIR /app

# Copy the app's output files from the build-env image
COPY --from=build /app/out .

# Media storage mount point, owned by the non-root app user (uid 1654 in
# chiseled images) so named volumes inherit writable ownership on first use.
# Chiseled images have no shell, so this must be a COPY, not RUN mkdir.
COPY --from=build --chown=1654:1654 /staging/media /app/media

# Expose the app's port (if needed)
EXPOSE 80

# Start the app
ENTRYPOINT ["dotnet", "Valour.Server.dll"]
