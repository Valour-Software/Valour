# ChatGPT made the first version of this, hopefully it works!

# Start with the official .NET Core 7.0 SDK image
FROM mcr.microsoft.com/dotnet/sdk:7.0.302 AS build-env

# Set the working directory to the app's source code directory
WORKDIR /app

# Copy the app's source code to the container image
COPY . .

# Restore the app's dependencies
RUN dotnet restore

# Build the app
RUN dotnet publish -c Release -o out

# Start with a smaller runtime image for the final image
FROM mcr.microsoft.com/dotnet/aspnet:7.0

# Set the working directory to the app's output directory
WORKDIR /app

# Copy the app's output files from the build-env image
COPY --from=build-env /app/out .

# Expose the app's port (if needed)
EXPOSE 80

# Start the app
ENTRYPOINT ["dotnet", "Valour.Server.dll"]
