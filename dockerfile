# ChatGPT made the first version of this, hopefully it works!

# Start with the official .NET Core 8.0 SDK image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

# Install Node.js (replace with the latest LTS version)
RUN curl -fsSL https://deb.nodesource.com/setup_18.x | bash - \
    && apt-get install -y nodejs

# Set the working directory to the app's source code directory
WORKDIR /app

# We need python for some reason
RUN apt-get update && apt-get install -y python3
RUN apt-get install libatomic1

# Copy the app's source code to the container image
COPY . .

# Restore workloads
RUN dotnet workload restore

# Restore the app's dependencies
RUN dotnet restore

# Build the app
RUN dotnet publish -c Release -o out

# Start with a smaller runtime image for the final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Set the working directory to the app's output directory
WORKDIR /app

# Copy the app's output files from the build-env image
COPY --from=build-env /app/out .

# Expose the app's port (if needed)
EXPOSE 80

# Start the app
ENTRYPOINT ["dotnet", "Valour.Server.dll"]
